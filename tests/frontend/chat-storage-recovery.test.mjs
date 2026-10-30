import test from 'node:test';
import assert from 'node:assert/strict';
import { createPinia, setActivePinia } from '../../web-frontend/node_modules/pinia/dist/pinia.mjs';
import { memoryChatStorage } from './helpers/chat-storage-memory.mjs';

const legacyKey = 'wepilot-conversations-v1';
const conversation = (id = 'history', content = '已保存的消息') => ({ id, title: '历史', updatedAt: 100, messages: [{ role: 'user', content }] });
let tabSequence = 0;
const newStore = async () => { const { useChatStore } = await import('../../web-frontend/src/stores/chat.js?storage-tab=' + ++tabSequence); setActivePinia(createPinia()); return useChatStore(); };
const initialized = async () => { const store = await newStore(); await store.init(); return store; };
const fixture = items => { const io = memoryChatStorage(items); io.install(); return io; };

test('a fallback write is recovered when IndexedDB reads succeed after refresh', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  store.current.messages.push({ role: 'assistant', content: '备用存储的新消息' });
  io.failures.write = 1; await store.persist();
  assert.match(store.persistenceWarning, /备用/);
  const refreshed = await initialized();
  assert.equal(refreshed.current.messages.at(-1).content, '备用存储的新消息');
});

test('a failed IndexedDB delete leaves a durable tombstone and a fallback warning', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  io.failures.write = 1; await store.remove('history');
  assert.match(store.persistenceWarning, /备用/);
  assert.equal((await initialized()).conversations.length, 0);
});

test('a failed IndexedDB clear survives refresh and blocks stale legacy snapshots', async () => {
  const io = fixture([conversation()]); io.localStorage.setItem(legacyKey, JSON.stringify([conversation()]));
  const store = await initialized(); io.failures.write = 1; await store.clear();
  assert.match(store.persistenceWarning, /备用/);
  assert.equal((await initialized()).conversations.length, 0);
});

test('delete failure in both stores keeps the conversation retryable and reports it', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  io.failures.write = 1; io.failures.localWrite = 100;
  await store.remove('history');
  assert.equal(store.current.id, 'history'); assert.equal(store.conversations.length, 1);
  assert.match(store.persistenceWarning, /未.*删除|删除.*失败/);
  io.failures.localWrite = 0; await store.remove('history');
  assert.equal((await initialized()).conversations.length, 0);
});

test('clear failure in both stores retains history and can be retried', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  io.failures.write = 1; io.failures.localWrite = 100;
  await store.clear();
  assert.equal(store.conversations.length, 1); assert.equal(store.current.id, 'history');
  assert.match(store.persistenceWarning, /未.*清空|清空.*失败/);
  io.failures.localWrite = 0; await store.clear();
  assert.equal((await initialized()).conversations.length, 0);
});

test('a successful primary write supersedes an old fallback even without touching recency', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  io.failures.write = 1; store.current.messages.push({ role: 'assistant', content: '旧备用' }); await store.persist();
  const before = store.current.updatedAt;
  store.current.messages.at(-1).content = '主库中更新的卡片'; await store.persist(store.current, { touch: false });
  assert.equal(store.current.updatedAt, before);
  io.failures.read = 1;
  assert.equal((await initialized()).current.messages.at(-1).content, '主库中更新的卡片');
});

test('a stale tab cannot recreate a conversation deleted in another tab', async () => {
  fixture([conversation()]); const first = await initialized(), stale = await initialized();
  await first.remove('history');
  stale.current.messages.push({ role: 'assistant', content: '旧窗口迟到消息' }); await stale.persist();
  assert.equal((await initialized()).conversations.length, 0);
  assert.equal(stale.findConversation('history'), null);
});

test('clear invalidates stale tab owners but newly created conversations still persist', async () => {
  fixture([conversation()]); const first = await initialized(), stale = await initialized();
  const old = stale.current; await first.clear();
  old.messages.push({ role: 'assistant', content: '迟到' }); await stale.persist(old);
  assert.equal((await initialized()).conversations.length, 0);
  await stale.create(); stale.current.messages.push({ role: 'user', content: '清空后的新对话' }); await stale.persist();
  const refreshed = await initialized(); assert.equal(refreshed.conversations.length, 1);
  assert.equal(refreshed.current.messages[0].content, '清空后的新对话');
});

test('a stale tab reports a write conflict instead of replacing newer messages', async () => {
  fixture([conversation()]); const first = await initialized(), stale = await initialized();
  first.current.messages.push({ role: 'assistant', content: '另一窗口的新消息' }); await first.persist();
  stale.current.messages.push({ role: 'assistant', content: '基于旧历史的消息' }); await stale.persist();
  assert.match(stale.persistenceWarning, /其他|另一|冲突|刷新/);
  assert.equal((await initialized()).current.messages.at(-1).content, '另一窗口的新消息');
});

test('queued saves and deletion cannot resurrect an owner after an in-flight write', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  const pause = io.pauseNextCommit(); const saving = store.persist(); await pause.reached;
  const old = store.current; old.messages.push({ role: 'assistant', content: '队列中消息' });
  const savingAgain = store.persist(old), deleting = store.remove(old.id);
  pause.resume(); await Promise.all([saving, savingAgain, deleting]);
  await store.persist(old); assert.equal((await initialized()).conversations.length, 0);
});

test('a primary read failure exposes recovery warning while retaining backup history', async () => {
  const io = fixture(); const store = await newStore(); store.current.messages.push({ role: 'user', content: '备用记录' });
  io.failures.write = 1; await store.persist(); io.failures.read = 1;
  const refreshed = await initialized(); assert.equal(refreshed.current.messages[0].content, '备用记录');
  assert.match(refreshed.persistenceWarning, /备用|数据库/);
});

test('malformed saved records do not break initialization or discard valid history', async () => {
  fixture([conversation(), { id: 'broken', messages: {} }]);
  const store = await initialized(); assert.equal(store.current.id, 'history');
  assert.equal(store.conversations.length, 1); assert.match(store.persistenceWarning, /损坏|无效/);
});

test('settings storage failure is reported without throwing out of the UI action', async () => {
  const io = fixture(); const store = await newStore(); io.failures.localWrite = 1;
  assert.doesNotThrow(() => store.saveSettings()); assert.match(store.persistenceWarning, /设置.*未|设置.*失败/);
});

test('both unreadable stores report history as unavailable rather than recovered', async () => {
  const io = fixture([conversation()]); io.failures.open = 1; io.failures.localRead = 100;
  const store = await initialized();
  assert.match(store.persistenceWarning, /尚未载入|无法读取|未能读取/);
  assert.doesNotMatch(store.persistenceWarning, /已.*恢复/);
});

test('blocked IndexedDB opening falls back without leaving initialization pending', async () => {
  const io = fixture(); io.localStorage.setItem(legacyKey, JSON.stringify([conversation()])); io.blockOpen();
  const store = await initialized(); assert.equal(store.current.id, 'history'); assert.match(store.persistenceWarning, /备用/);
});

test('a primary-only tombstone blocks a stale tab when backup writes are unavailable', async () => {
  const io = fixture([conversation()]); const first = await initialized(), stale = await initialized();
  io.failures.localWrite = 100; await first.remove('history');
  stale.current.messages.push({ role: 'assistant', content: '迟到' }); await stale.persist();
  assert.equal(stale.findConversation('history'), null);
  assert.equal((await initialized()).conversations.length, 0);
});

test('a primary-only clear marker blocks a stale tab when backup writes are unavailable', async () => {
  const io = fixture([conversation()]); const first = await initialized(), stale = await initialized();
  io.failures.localWrite = 100; await first.clear();
  stale.current.messages.push({ role: 'assistant', content: '迟到' }); await stale.persist();
  assert.equal((await initialized()).conversations.length, 0);
});

test('fallback writes from independent tabs retain both conversation IDs', async () => {
  const io = fixture(); const first = await newStore(), second = await newStore();
  first.current.messages.push({ role: 'user', content: '窗口一' }); second.current.messages.push({ role: 'user', content: '窗口二' });
  io.failures.open = 2; await Promise.all([first.persist(), second.persist()]);
  assert.deepEqual((await initialized()).conversations.map(item => item.messages[0].content).sort(), ['窗口一', '窗口二']);
});

test('a later success for another conversation does not erase an unsaved warning', async () => {
  const io = fixture([conversation('first'), conversation('second')]); const store = await initialized();
  io.failures.write = 1; io.failures.localWrite = 1;
  await store.persist(store.findConversation('first'));
  assert.match(store.persistenceWarning, /尚未保存/);
  await store.persist(store.findConversation('second'));
  assert.match(store.persistenceWarning, /尚未保存/);
  await store.persist(store.findConversation('first'));
  assert.equal(store.persistenceWarning, '');
});

test('a stale backup ancestor is not treated as a conflict after intermediate primary versions are replaced', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  store.current.messages.push({ role: 'assistant', content: 'A' }); await store.persist();
  io.failures.localWrite = 100;
  store.current.messages.push({ role: 'assistant', content: 'B' }); await store.persist();
  store.current.messages.push({ role: 'assistant', content: 'C' }); await store.persist();
  io.failures.localWrite = 0;
  const refreshed = await initialized();
  assert.equal(refreshed.current.messages.at(-1).content, 'C');
  assert.doesNotMatch(refreshed.persistenceWarning, /其他窗口|冲突/);
  refreshed.current.messages.push({ role: 'assistant', content: 'D' }); await refreshed.persist();
  assert.equal(refreshed.persistenceWarning, '');
  assert.equal((await initialized()).current.messages.at(-1).content, 'D');
});

for (const action of ['remove', 'clear']) test(`a queued save blocked by a failed ${action} preserves the unsaved owner and allows retry`, async () => {
  const io = fixture([conversation()]); const store = await initialized();
  store.current.messages.push({ role: 'assistant', content: '尚未保存的消息' });
  io.failures.write = 1; io.failures.localWrite = 100;
  const saving = store.persist();
  const deleting = action === 'remove' ? store.remove('history') : store.clear();
  const [, deleted] = await Promise.all([saving, deleting]);
  assert.equal(deleted, false);
  assert.equal(store.current.id, 'history'); assert.equal(store.conversations.length, 1);
  assert.equal(store.current.messages.at(-1).content, '尚未保存的消息');
  assert.match(store.persistenceWarning, action === 'remove' ? /尚未删除/ : /尚未清空/);
  io.failures.localWrite = 0;
  await store.persist();
  assert.equal((await initialized()).current.messages.at(-1).content, '尚未保存的消息');
});

test('an owner created while both stores are unreadable can retry after an earlier clear', async () => {
  const io = fixture([conversation()]); await (await initialized()).clear();
  io.failures.open = 100; io.failures.localRead = 100; io.failures.localWrite = 100;
  const store = await initialized(); const owner = store.current.id;
  store.current.messages.push({ role: 'user', content: '存储恢复后需要保留的新消息' }); await store.persist();
  assert.match(store.persistenceWarning, /尚未保存/);
  io.failures.open = 0; io.failures.localRead = 0; io.failures.localWrite = 0;
  await store.persist();
  assert.equal(store.current.id, owner); assert.ok(store.findConversation(owner));
  assert.equal((await initialized()).current.messages[0].content, '存储恢复后需要保留的新消息');
});

test('reinitializing while both stores are unreadable retains the in-memory current conversation', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  store.current.messages.push({ role: 'assistant', content: '内存中尚未保存' });
  io.failures.open = 100; io.failures.localRead = 100;
  await store.init();
  assert.equal(store.current.id, 'history'); assert.equal(store.current.messages.at(-1).content, '内存中尚未保存');
  assert.match(store.persistenceWarning, /尚未载入/);
});

test('primary-only saves preserve ancestor proof while backup reads are temporarily unavailable', async () => {
  const io = fixture([conversation()]); const store = await initialized();
  store.current.messages.push({ role: 'assistant', content: 'A' }); await store.persist();
  io.failures.localRead = 100; io.failures.localWrite = 100;
  for (const content of ['B', 'C']) { store.current.messages.push({ role: 'assistant', content }); await store.persist(); }
  io.failures.localRead = 0; io.failures.localWrite = 0;
  const refreshed = await initialized(); assert.doesNotMatch(refreshed.persistenceWarning, /其他窗口|冲突/);
  refreshed.current.messages.push({ role: 'assistant', content: 'D' }); await refreshed.persist();
  assert.equal((await initialized()).current.messages.at(-1).content, 'D');
});

test('independent branches still conflict when one tab cannot observe a primary-only write', async () => {
  const io = fixture([conversation()]); const first = await initialized();
  first.current.messages.push({ role: 'assistant', content: '共同祖先A' }); await first.persist();
  const stale = await initialized();
  io.failures.localWrite = 1;
  first.current.messages.push({ role: 'assistant', content: '主库分支B' }); await first.persist();
  io.failures.open = 1;
  stale.current.messages.push({ role: 'assistant', content: '备用分支C' }); await stale.persist();
  const refreshed = await initialized(); assert.match(refreshed.persistenceWarning, /其他窗口|冲突/);
  refreshed.current.messages.push({ role: 'assistant', content: '冲突后不应覆盖D' }); await refreshed.persist();
  assert.match(refreshed.persistenceWarning, /尚未保存/);
  assert.equal(io.records.get('history').messages.at(-1).content, '主库分支B');
});
