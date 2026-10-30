import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {spawnSync} from 'node:child_process';
import * as runtime from '../../web-frontend/src/services/native-dataset.js';

const fields = ['datasetId', 'demandVersion', 'fixtureId', 'inputFingerprint'];
const identities = Object.fromEntries(['small13', 'large85'].map(datasetId => {
  const path = datasetId === 'small13' ? '../../agent-backend/Data/native-demo.json' : '../../agent-backend/Data/datasets/large85/native-demo.json';
  const fixture = JSON.parse(readFileSync(new URL(path, import.meta.url), 'utf8'));
  return [datasetId, Object.fromEntries(fields.map(field => [field, field === 'datasetId' ? datasetId : fixture[field]]))];
}));

function checkRejected(actual, expected) {
  assert.equal(typeof runtime.validateDatasetIdentity, 'function', '前端尚未校验API数据集身份');
  const result = runtime.validateDatasetIdentity(actual, expected);
  assert.equal(result.ok, false);
  assert.equal(result.refreshRequired, true);
  assert.match(result.message, /刷新页面/);
  assert.doesNotMatch(result.message, /新建对话/);
  assert.throws(() => runtime.assertDatasetIdentity(actual, expected), error => {
    assert.equal(error.code, 'DATASET_REFRESH_REQUIRED');
    assert.equal(error.refreshRequired, true);
    assert.equal(error.message, result.message);
    return true;
  });
}

test('both dataset identities pass only when all four fields match', () => {
  assert.equal(typeof runtime.validateDatasetIdentity, 'function', '前端尚未校验API数据集身份');
  assert.equal(typeof runtime.assertDatasetIdentity, 'function');
  for (const expected of Object.values(identities)) {
    const actual = {...expected, diagnostic: 'health may contain extra fields'};
    const before = structuredClone({actual, expected});
    Object.freeze(actual);
    Object.freeze(expected);
    const result = runtime.validateDatasetIdentity(actual, expected);
    assert.equal(result.ok, true);
    assert.equal(result.refreshRequired, false);
    assert.deepEqual(runtime.assertDatasetIdentity(actual, expected), expected);
    assert.deepEqual({actual, expected}, before, '身份校验不得修改API或前端身份');
  }
});

test('default comparison uses the selected frontend identity for both dataset selections', () => {
  for (const dataset of Object.keys(identities)) {
    const child = spawnSync(process.execPath, ['--input-type=module', '-e', `
      import assert from 'node:assert/strict';
      import * as runtime from './web-frontend/src/services/native-dataset.js';
      assert.equal(typeof runtime.assertDatasetIdentity, 'function', '前端尚未校验API数据集身份');
      const expected = ${JSON.stringify(identities[dataset])};
      assert.deepEqual(runtime.nativeDatasetIdentity, expected);
      assert.deepEqual(runtime.assertDatasetIdentity(expected), expected);
      const other = ${JSON.stringify(identities[dataset === 'small13' ? 'large85' : 'small13'])};
      assert.throws(() => runtime.assertDatasetIdentity(other), /刷新页面/);
    `], {cwd: new URL('../../', import.meta.url), env: {...process.env, APS_DEMO_DATASET: dataset}, encoding: 'utf8'});
    assert.equal(child.status, 0, child.stderr);
  }
});

test('missing, malformed or blank API identities require a page refresh', () => {
  const expected = identities.small13;
  for (const actual of [undefined, null, {}, [], 'small13']) checkRejected(actual, expected);
  for (const field of fields) {
    const missing = {...expected};
    delete missing[field];
    checkRejected(missing, expected);
    for (const value of [null, '', '  ', 1, false]) checkRejected({...expected, [field]: value}, expected);
  }
});

test('incomplete frontend identities cannot bypass comparison', () => {
  const actual = identities.large85;
  for (const expected of [null, {}, []]) checkRejected(actual, expected);
  for (const field of fields) {
    const expected = {...actual};
    delete expected[field];
    checkRejected(actual, expected);
    checkRejected({...actual, [field]: ''}, {...actual, [field]: ''});
  }
});

test('unknown dataset IDs are rejected even if both sides report the same ID', () => {
  for (const datasetId of ['unknown', '../large85', 'SMALL13']) {
    const unknown = {...identities.small13, datasetId};
    checkRejected(unknown, identities.small13);
    checkRejected(unknown, unknown);
  }
});

test('switching between the two datasets requires a page refresh in either direction', () => {
  checkRejected(identities.small13, identities.large85);
  checkRejected(identities.large85, identities.small13);
});

test('a changed version, fixture or fingerprint requires a refresh despite an unchanged dataset ID', () => {
  for (const expected of Object.values(identities)) {
    for (const field of fields.slice(1)) checkRejected({...expected, [field]: expected[field] + '-stale'}, expected);
  }
});

test('an earlier mismatch does not poison a subsequent matching identity', () => {
  checkRejected(identities.small13, identities.large85);
  assert.equal(runtime.validateDatasetIdentity(identities.large85, identities.large85).ok, true);
  assert.deepEqual(runtime.assertDatasetIdentity(identities.small13, identities.small13), identities.small13);
});

const health = (changes = {}) => ({status: 'ok', mode: 'model', configured: true, dataset: {...runtime.nativeDatasetIdentity}, ...changes});

test('connection verification makes one uncached GET and returns complete matching health data', async () => {
  assert.equal(typeof runtime.verifyDatasetConnection, 'function', '请求前尚未验证API数据集身份');
  for (const mode of ['demo', 'model']) {
    const expected = health({mode});
    const requests = [];
    const result = await runtime.verifyDatasetConnection({fetcher: async (...args) => {
      requests.push(args);
      return {ok: true, json: async () => expected};
    }});
    assert.strictEqual(result, expected);
    assert.equal(requests.length, 1);
    const [url, options] = requests[0];
    assert.equal(url, '/api/agent/health');
    assert.equal(options.method, 'GET');
    assert.equal(options.cache, 'no-store');
    assert.ok(options.signal instanceof AbortSignal);
    assert.equal(options.signal.aborted, false);
  }
});

test('connection verification forwards an operation cancellation signal unchanged', async () => {
  assert.equal(typeof runtime.verifyDatasetConnection, 'function', '请求前尚未验证API数据集身份');
  const controller = new AbortController();
  await runtime.verifyDatasetConnection({signal: controller.signal, fetcher: async (url, options) => {
    assert.strictEqual(options.signal, controller.signal);
    return {ok: true, json: async () => health()};
  }});
});

test('connection verification preserves dataset refresh errors instead of disguising them as network failures', async () => {
  assert.equal(typeof runtime.verifyDatasetConnection, 'function', '请求前尚未验证API数据集身份');
  for (const dataset of [undefined, {...runtime.nativeDatasetIdentity, inputFingerprint: 'old-fingerprint'}, {...runtime.nativeDatasetIdentity, datasetId: 'unknown'}]) {
    await assert.rejects(runtime.verifyDatasetConnection({fetcher: async () => ({ok: true, json: async () => health({dataset})})}), error => {
      assert.equal(error.code, 'DATASET_REFRESH_REQUIRED');
      assert.equal(error.refreshRequired, true);
      assert.match(error.message, /刷新页面/);
      return true;
    });
  }
});

test('network, HTTP, JSON and invalid health failures remain ordinary connection errors', async () => {
  assert.equal(typeof runtime.verifyDatasetConnection, 'function', '请求前尚未验证API数据集身份');
  const fetchers = [
    async () => { throw new TypeError('sensitive transport detail'); },
    async () => ({ok: false, status: 503, json: async () => health()}),
    async () => ({ok: true, json: async () => { throw new SyntaxError('invalid JSON'); }}),
    ...[null, {}, health({status: 'starting'}), health({mode: 'unknown'})].map(value => async () => ({ok: true, json: async () => value})),
  ];
  for (const fetcher of fetchers) {
    await assert.rejects(runtime.verifyDatasetConnection({fetcher}), error => {
      assert.equal(error.code, undefined);
      assert.notEqual(error.refreshRequired, true);
      assert.match(error.message, /连接失败/);
      assert.doesNotMatch(error.message, /sensitive|SyntaxError|刷新页面/);
      return true;
    });
  }
});

test('cancelled verification cannot accept a late healthy response', async () => {
  assert.equal(typeof runtime.verifyDatasetConnection, 'function', '请求前尚未验证API数据集身份');
  const controller = new AbortController();
  const reason = new DOMException('cancelled operation', 'AbortError');
  await assert.rejects(runtime.verifyDatasetConnection({signal: controller.signal, fetcher: async () => ({ok: true, json: async () => {
    controller.abort(reason);
    return health();
  }})}), error => error === reason);
  let requests = 0;
  await assert.rejects(runtime.verifyDatasetConnection({signal: controller.signal, fetcher: async () => { requests++; return {ok: true, json: async () => health()}; }}), error => error === reason);
  assert.equal(requests, 0, '已取消的操作不得再发起健康请求');
});
