import { createId } from './id.js';

const LEGACY_KEY = 'wepilot-conversations-v1';
const PREFIX = 'wepilot-chat-record-v2:';
const CLEAR_ID = '__wepilot_chat_clear_v2__';
const clone = value => JSON.parse(JSON.stringify(value));
const validConversation = value => typeof value?.id === 'string' && value.id !== CLEAR_ID &&
  Array.isArray(value.messages) && value.messages.every(message => message && typeof message === 'object');
const metadata = record => record._chatStorage;
const compare = (a, b) => Number(metadata(a).modern) - Number(metadata(b).modern) ||
  metadata(a).clock - metadata(b).clock || metadata(a).version.localeCompare(metadata(b).version);
const conflictWarning = '此会话与其他窗口的记录存在冲突，本次修改尚未保存。请先导出当前会话，再新建对话继续；已有记录已保留。';

// Legacy snapshots had no revision. A content fingerprint detects stale owners
// without changing updatedAt, which belongs to message recency, not storage I/O.
function legacyVersion(value) {
  let hash = 2166136261;
  for (const char of JSON.stringify(value)) hash = Math.imul(hash ^ char.charCodeAt(0), 16777619);
  return `legacy:${hash >>> 0}`;
}
function normalize(value) {
  if (value?._chatStorage?.format === 2) {
    const info = metadata(value);
    if (typeof info.version !== 'string' || !Number.isFinite(info.clock) || typeof info.epoch !== 'string') return null;
    if (info.ancestors !== undefined && (!Array.isArray(info.ancestors) || info.ancestors.some(version => typeof version !== 'string'))) return null;
    if (info.kind === 'clear' ? value.id !== CLEAR_ID : info.kind === 'delete' ? typeof value.id !== 'string' : info.kind !== 'put' || !validConversation(value)) return null;
    return { ...value, _chatStorage: { ...info, modern: true } };
  }
  return validConversation(value) ? { ...value, _chatStorage: { kind: 'put', modern: false, version: legacyVersion(value), clock: Number(value.updatedAt) || 0, epoch: '' } } : null;
}
function backupRecords() {
  const entries = [], records = [];
  let unavailable = false, invalid = false;
  try {
    const legacy = localStorage.getItem(LEGACY_KEY);
    if (legacy !== null) {
      try { const items = JSON.parse(legacy); if (Array.isArray(items)) records.push(...items); else invalid = true; } catch { invalid = true; }
    }
    // Snapshot keys first: another tab can add/remove entries while we read.
    const keys = [];
    for (let i = 0; i < localStorage.length; i++) { const key = localStorage.key(i); if (key?.startsWith(PREFIX)) keys.push(key); }
    for (const key of keys) {
      let text;
      try { text = localStorage.getItem(key); } catch { unavailable = true; continue; }
      try { if (text !== null) { const record = JSON.parse(text); records.push(record); entries.push({ key, record }); } } catch { invalid = true; }
    }
  } catch { unavailable = true; }
  return { records, entries, unavailable, invalid };
}
function mergeRecords(primary, backup) {
  const groups = new Map();
  let invalid = backup.invalid, clock = 0, clear = null;
  for (const value of [...primary, ...backup.records]) {
    const record = normalize(value);
    if (!record) { invalid = true; continue; }
    clock = Math.max(clock, metadata(record).clock);
    if (metadata(record).kind === 'clear') { if (!clear || compare(record, clear) > 0) clear = record; continue; }
    const group = groups.get(record.id) || new Map();
    group.set(metadata(record).version, record); groups.set(record.id, group);
  }
  const epoch = clear ? metadata(clear).version : '', records = new Map(), deleted = new Set(), conflicts = new Set();
  for (const [id, group] of groups) {
    const values = [...group.values()];
    if (values.some(record => metadata(record).kind === 'delete')) { deleted.add(id); continue; }
    const eligible = values.filter(record => metadata(record).epoch === epoch).sort(compare);
    if (!eligible.length) continue;
    const latest = eligible.at(-1);
    const parents = new Set(eligible.flatMap(record => [metadata(record).baseVersion, ...(metadata(record).ancestors || [])]));
    const heads = eligible.filter(record => metadata(record).modern && !parents.has(metadata(record).version));
    if (heads.length > 1) conflicts.add(id);
    records.set(id, latest);
  }
  return { records, deleted, conflicts, epoch, clock, invalid };
}
async function database(mode, action) {
  const db = await new Promise((resolve, reject) => {
    let settled = false;
    const request = indexedDB.open('wepilot', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('conversations', { keyPath: 'id' });
    request.onsuccess = () => { if (settled) request.result.close(); else { settled = true; resolve(request.result); } };
    request.onerror = request.onblocked = () => { settled = true; reject(request.error || Error('会话数据库暂时不可用')); };
  });
  return new Promise((resolve, reject) => {
    let tx, result, failure;
    try {
      tx = db.transaction('conversations', mode);
      const store = tx.objectStore('conversations'), request = store.getAll();
      request.onsuccess = () => {
        try { result = action(request.result, store); } catch (error) { failure = error; tx.abort(); }
      };
    } catch (error) { db.close(); reject(error); return; }
    tx.oncomplete = () => { db.close(); resolve(result); };
    tx.onerror = tx.onabort = () => { db.close(); reject(failure || tx.error || Error('会话数据库操作失败')); };
  });
}

export function browserChatStorage() {
  let queue = Promise.resolve(), cached = [], hasPrimarySnapshot = false;
  const versions = new Map(), ownerEpochs = new Map(), blocked = new Set(), unsavedWarnings = new Map();
  const enqueue = action => { const task = queue.catch(() => {}).then(action); queue = task; return task; };
  const observe = state => {
    for (const [id, record] of state.records) { versions.set(id, metadata(record).version); ownerEpochs.set(id, state.epoch); }
  };
  const track = id => {
    if (!ownerEpochs.has(id)) {
      const backup = backupRecords();
      ownerEpochs.set(id, hasPrimarySnapshot || !backup.unavailable ? mergeRecords(cached, backup).epoch : null);
      versions.set(id, null);
    }
  };
  const isDeleted = id => {
    if (blocked.has(id)) return true;
    const state = mergeRecords(cached, backupRecords());
    return state.deleted.has(id) || ownerEpochs.has(id) && ownerEpochs.get(id) !== null && ownerEpochs.get(id) !== state.epoch;
  };
  const mutate = (kind, item) => enqueue(async () => {
    const id = kind === 'clear' ? CLEAR_ID : typeof item === 'string' ? item : item.id;
    const finish = result => {
      if (result.saved || result.deleted) { if (kind === 'clear') unsavedWarnings.clear(); else unsavedWarnings.delete(id); }
      else unsavedWarnings.set(id, result.warning);
      return { ...result, warning: unsavedWarnings.values().next().value || result.warning };
    };
    let record, backup, state, backedUp = false, skipped;
    const apply = (primary, store) => {
      backup = backupRecords(); state = mergeRecords(primary, backup);
      if (!store && backup.unavailable) { skipped = 'unavailable'; return; }
      if (kind === 'put') {
        if (blocked.has(id)) { skipped = 'blocked'; return; }
        if (ownerEpochs.get(id) === null) ownerEpochs.set(id, state.epoch);
        if (state.deleted.has(id) || ownerEpochs.get(id) !== state.epoch) { skipped = 'deleted'; return; }
        if (state.conflicts.has(id) || (metadata(state.records.get(id) || { _chatStorage: {} }).version || null) !== versions.get(id)) { skipped = 'conflict'; return; }
      }
      const parent = state.records.get(id)?._chatStorage;
      const provenAncestors = new Set([parent?.version, ...(parent?.ancestors || [])]);
      const backupVersions = new Set(backup.records.map(normalize).filter(value => value?.id === id).map(value => metadata(value).version));
      // Keep proof for ancestors still present in backup, even when IDB has
      // replaced several intermediate versions. This shrinks after backup
      // cleanup rather than growing with the lifetime number of saves.
      // During a read outage an older backup may still exist but cannot be
      // enumerated. Keep the proofs until a complete read can compact them.
      const ancestors = kind === 'put' ? [...provenAncestors].filter(version => version && (backup.unavailable || backupVersions.has(version))) : [];
      record = { ...(kind === 'put' ? item : { id }), _chatStorage: {
        format: 2, modern: true, kind, version: createId(), clock: Math.max(Date.now(), state.clock + 1), epoch: state.epoch,
        baseVersion: kind === 'put' ? versions.get(id) : null, ancestors,
      } };
      // An immutable per-revision key avoids replacing another tab's backup.
      // Write it before the IDB commit so an aborted transaction is recoverable.
      try { localStorage.setItem(PREFIX + record._chatStorage.version, JSON.stringify(record)); backedUp = true; } catch {}
      if (store) { if (kind === 'clear') store.clear(); store.put(record); }
    };
    let primarySaved = false;
    try {
      await database('readwrite', (primary, store) => { cached = primary; hasPrimarySnapshot = true; apply(primary, store); });
      primarySaved = true;
    } catch {
      if (!record && !skipped) apply(cached);
    }
    if (skipped === 'blocked') return { saved: false, blocked: true };
    if (skipped === 'deleted' || skipped === 'conflict') return finish({ saved: false, deleted: skipped === 'deleted', warning: skipped === 'conflict' ? conflictWarning : '此会话已删除或清空，迟到内容未保存。' });
    if (!primarySaved && !backedUp) return finish({ saved: false, warning: kind === 'delete' ? '浏览器存储不可用，会话尚未删除，请重试。' : kind === 'clear' ? '浏览器存储不可用，历史会话尚未清空，请重试。' : '浏览器存储不可用，本次会话尚未保存，请重试。' });
    cached = kind === 'clear' ? [record] : [...cached.filter(value => value.id !== id), record];
    if (kind === 'put') { versions.set(id, metadata(record).version); ownerEpochs.set(id, state.epoch); }
    // Remove only revisions actually observed by this operation. New concurrent
    // keys remain intact. A durable marker is retained for deletes and clears.
    if (backedUp) {
      for (const entry of backup.entries) if (kind === 'clear' || entry.record?.id === id) { try { localStorage.removeItem(entry.key); } catch {} }
      if (kind === 'clear') { try { localStorage.removeItem(LEGACY_KEY); } catch {} }
    }
    return finish({ saved: true, warning: primarySaved ? '' : '本地数据库不可用，已使用备用存储。' });
  });
  return {
    track, isDeleted,
    block(id) { blocked.add(id); },
    unblock(id) { blocked.delete(id); },
    read: () => enqueue(async () => {
      let primaryUnavailable = false;
      try { cached = await database('readonly', values => values); hasPrimarySnapshot = true; } catch { primaryUnavailable = true; }
      const backup = backupRecords(), state = mergeRecords(cached, backup);
      const available = !primaryUnavailable || !backup.unavailable;
      if (available) observe(state);
      const recovered = [...state.records.values()].some(record => metadata(record).modern && !cached.some(value => value._chatStorage?.version === metadata(record).version));
      const warning = primaryUnavailable && backup.unavailable ? '浏览器存储不可用，历史会话尚未载入，请恢复存储后刷新。' : state.conflicts.size ? conflictWarning : state.invalid ? '部分会话记录无效，已保留其余历史记录。' : primaryUnavailable ? '本地数据库不可用，已从备用存储恢复会话。' : backup.unavailable ? '备用存储不可用，已载入本地数据库会话。' : recovered ? '已从备用存储恢复较新的会话。' : '';
      return { conversations: [...state.records.values()].map(record => { const { _chatStorage, ...item } = record; return clone(item); }), warning, available };
    }),
    save(item) { track(item.id); return mutate('put', item); },
    remove: id => mutate('delete', id),
    clear: () => mutate('clear'),
  };
}
