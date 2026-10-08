import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { createId } from '../../web-frontend/src/services/id.js';

test('LAN HTTP without randomUUID can create distinct UUIDs', () => {
  const original = Object.getOwnPropertyDescriptor(globalThis, 'crypto');
  Object.defineProperty(globalThis, 'crypto', {
    configurable: true,
    value: { getRandomValues: bytes => webcrypto.getRandomValues(bytes) },
  });
  try {
    const ids = Array.from({ length: 100 }, () => createId());
    assert.equal(new Set(ids).size, ids.length);
    for (const id of ids)
      assert.match(id, /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  } finally {
    if (original) Object.defineProperty(globalThis, 'crypto', original);
    else delete globalThis.crypto;
  }
});
