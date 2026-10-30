import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {spawnSync} from 'node:child_process';

let api = {};
try { api = await import('../../dev-config/native-demo/dataset-selection.mjs'); }
catch (error) { if (error.code !== 'ERR_MODULE_NOT_FOUND') throw error; }

test('project selection and a one-process override choose only known datasets', () => {
  assert.equal(typeof api.resolveDataset, 'function', '数据集选择尚未实现');
  assert.equal(api.resolveDataset(undefined, {active:'large85'}).id, 'large85');
  assert.equal(api.resolveDataset('small13', {active:'large85'}).id, 'small13');
  assert.equal(api.resolveDataset('large85', {active:'small13'}).id, 'large85');
  for (const value of ['unknown', '../large85', 'SMALL13']) assert.throws(() => api.resolveDataset(value, {active:'large85'}), /数据集/);
  assert.throws(() => api.resolveDataset(undefined, {active:'unknown'}), /数据集/);
});

test('small runtime keeps its existing path while large runtime is separate', () => {
  assert.equal(typeof api.resolveDataset, 'function', '数据集选择尚未实现');
  const small = api.resolveDataset('small13'), large = api.resolveDataset('large85');
  assert.equal(small.dataDirectory, 'agent-backend/Data');
  assert.equal(small.runtimeDirectory, '');
  assert.equal(large.dataDirectory, 'agent-backend/Data/datasets/large85');
  assert.equal(large.runtimeDirectory, 'datasets/large85');
});

test('Node plan selection uses the same configured dataset without changing the small fixture', () => {
  for (const [dataset, count] of [['small13',12], ['large85',84]]) {
    const child = spawnSync(process.execPath, ['--input-type=module', '-e', "import {initialDemoPlan} from './web-frontend/src/services/demo-plan.js';process.stdout.write(JSON.stringify(initialDemoPlan()))"], {cwd:new URL('../../',import.meta.url),env:{...process.env,APS_DEMO_DATASET:dataset},encoding:'utf8'});
    assert.equal(child.status, 0, child.stderr);
    const plan = JSON.parse(child.stdout);
    assert.equal(plan.orderIds.length, count, dataset);
    const source = JSON.parse(readFileSync(new URL(dataset === 'small13' ? '../../agent-backend/Data/native-demo.json' : '../../agent-backend/Data/datasets/large85/native-demo.json', import.meta.url),'utf8'));
    assert.equal(plan.demandVersion, source.demandVersion);
    assert.equal(plan.nativeResultId, source.baselineResultId);
  }
});

test('raw workspace associations are offered only for cards from the current dataset', () => {
  const source = readFileSync(new URL('../../web-frontend/src/App.vue',import.meta.url),'utf8');
  const context = source.match(/<DemoWorkspaceContext v-if="([^"]+)"/);
  assert.match(context?.[1] || '', /block\.spec\.demandVersion\s*===\s*currentPlan\?\.demandVersion/);
});

test('workspace storage reads and retains the selected version without overwriting another dataset', () => {
  const code = `
    import {initialDemoPlan} from './web-frontend/src/services/demo-plan.js';
    import {nativeDemo} from './web-frontend/src/services/native-dataset.js';
    import {browserPlanRepository,createWorkspacePlan} from './web-frontend/src/services/workspace-plan.js';
    const initial=initialDemoPlan();
    const plan={...initial,orderIds:[nativeDemo.insertionOrders[0].orderId,...initial.orderIds],revision:7,nativeResultId:'e'.repeat(32),nativeMrpResultId:'f'.repeat(32),releaseStatus:'released',nativeReleaseId:'a'.repeat(32),releasedCalculationId:'e'.repeat(32)};
    const other={id:'other-dataset-version',plan:{preserve:'user-record'},updatedAt:3};
    const records=new Map([[plan.demandVersion,{id:plan.demandVersion,plan,updatedAt:9}],[other.id,other]]),keys=[];
    globalThis.indexedDB={open(){const request={};queueMicrotask(()=>{request.result={close(){},transaction(){const tx={objectStore(){return {get(key){keys.push(key);const get={};queueMicrotask(()=>{get.result=records.get(key);get.onsuccess();queueMicrotask(()=>tx.oncomplete?.());});return get;},put(record){records.set(record.id,structuredClone(record));}};}};return tx;}};request.onsuccess();});return request;}};
    const workspace=createWorkspacePlan(browserPlanRepository());
    const recovered=await workspace.init([]);
    if(JSON.stringify(recovered)!==JSON.stringify(plan))throw Error('采用、MRP或下发状态丢失');
    if(JSON.stringify(records.get(other.id))!==JSON.stringify(other))throw Error('另一数据集被覆盖');
    if(keys.some(key=>key!==plan.demandVersion)||records.size!==2)throw Error('未按版本隔离');
    process.stdout.write(JSON.stringify({version:plan.demandVersion,orders:recovered.orderIds.length}));
  `;
  const versions=[];
  for (const [dataset,count] of [['small13',13],['large85',85]]) {
    const child=spawnSync(process.execPath,['--input-type=module','-e',code],{cwd:new URL('../../',import.meta.url),env:{...process.env,APS_DEMO_DATASET:dataset},encoding:'utf8'});
    assert.equal(child.status,0,child.stderr);
    const state=JSON.parse(child.stdout);assert.equal(state.orders,count);versions.push(state.version);
  }
  assert.notEqual(versions[0],versions[1]);
});
