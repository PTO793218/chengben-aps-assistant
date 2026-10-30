// Browser I/O only: tests still execute the real Pinia chat store.
const clone = value => value === undefined ? undefined : structuredClone(value);

export function memoryChatStorage(conversations = []) {
  const records = new Map(conversations.map(item => [item.id, clone(item)]));
  const values = new Map();
  const failures = { open: 0, read: 0, write: 0, localRead: 0, localWrite: 0 };
  let tail = Promise.resolve(), nextCommit = null, blocked = false;
  const consume = kind => {
    if (failures[kind] > 0) { failures[kind]--; throw new Error(`Injected ${kind} failure`); }
  };
  const localStorage = {
    get length() { consume('localRead'); return values.size; },
    key(index) { consume('localRead'); return [...values.keys()][index] ?? null; },
    getItem(key) { consume('localRead'); return values.get(key) ?? null; },
    setItem(key, value) { consume('localWrite'); values.set(key, String(value)); },
    removeItem(key) { consume('localWrite'); values.delete(key); },
  };
  const indexedDB = {
    open() {
      const request = {};
      queueMicrotask(() => {
        if (blocked) { request.onblocked?.(); return; }
        try { consume('open'); } catch (error) { request.error = error; request.onerror?.(); return; }
        request.result = {
          close() {},
          transaction(_name, mode) {
            const actions = [];
            let aborted = false;
            const tx = { error: null, abort() { aborted = true; }, objectStore() {
              const enqueue = (kind, action) => {
                const req = {};
                actions.push(() => {
                  consume(kind);
                  req.result = action(working);
                  req.onsuccess?.();
                });
                return req;
              };
              return {
                getAll: () => enqueue('read', map => clone([...map.values()])),
                get: id => enqueue('read', map => clone(map.get(id))),
                put: value => enqueue('write', map => { map.set(value.id, clone(value)); return value.id; }),
                delete: id => enqueue('write', map => map.delete(id)),
                clear: () => enqueue('write', map => map.clear()),
              };
            } };
            let working;
            const run = async () => {
              working = new Map([...records].map(([id, value]) => [id, clone(value)]));
              try {
                for (let i = 0; i < actions.length; i++) { if (aborted) throw new Error('Aborted'); actions[i](); }
                if (mode === 'readwrite' && nextCommit) {
                  const pause = nextCommit; nextCommit = null;
                  pause.started(); await pause.wait;
                }
                if (aborted) throw new Error('Aborted');
                if (mode === 'readwrite') { records.clear(); for (const [id, value] of working) records.set(id, value); }
                tx.oncomplete?.();
              } catch (error) { tx.error = error; tx.onabort?.(); }
            };
            tail = tail.then(run, run);
            return tx;
          },
        };
        request.onsuccess?.();
      });
      return request;
    },
  };
  return {
    records, values, failures, indexedDB, localStorage,
    install() { globalThis.indexedDB = indexedDB; globalThis.localStorage = localStorage; },
    blockOpen(value = true) { blocked = value; },
    pauseNextCommit() {
      let resume, started;
      const wait = new Promise(resolve => resume = resolve);
      const reached = new Promise(resolve => started = resolve);
      nextCommit = { wait, started };
      return { reached, resume };
    },
  };
}
