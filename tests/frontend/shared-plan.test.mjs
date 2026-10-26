import test from 'node:test';
import assert from 'node:assert/strict';
import {initialDemoPlan,adoptDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
let api={};try{api=await import('../../web-frontend/src/services/workspace-plan.js');}catch(e){if(e.code!=='ERR_MODULE_NOT_FOUND')throw e;}
const clone=x=>structuredClone(x),factory=()=>{assert.equal(typeof api.createWorkspacePlan,'function','工作区计划尚未实现');return api.createWorkspacePlan;};
function repository(){let record=null,tail=Promise.resolve();return{read:async()=>clone(record),transact(fn){const work=tail.then(()=>{const next=fn(clone(record));record=clone(next);return clone(next);});tail=work.catch(()=>{});return work;}};}
function result(p,view='insert',token='a'){
 const proposedPlan={...clone(p),revision:p.revision+1,nativeResultId:token.repeat(32)};
 if(view==='insert')proposedPlan.orderIds=['Z99100014',...p.orderIds];
 if(view==='reschedule')proposedPlan.orderIds=['Z9900025',...p.orderIds.filter(x=>x!=='Z9900025')];
 if(view==='mrp'){proposedPlan.nativeResultId=p.nativeResultId;proposedPlan.nativeMrpResultId=token.repeat(32);}
 return{native:true,view,orderId:view==='insert'?'Z99100014':'Z9900025',baseState:clone(p),baseRevision:p.revision,demandVersion:p.demandVersion,proposedPlan,calculationId:proposedPlan.nativeResultId,mrpResultId:proposedPlan.nativeMrpResultId};
}
test('adopted thirteen-order plan survives new conversations, switching and a fresh workspace instance',async()=>{
 const make=factory(),repo=repository(),a=make(repo),p=await a.init([]),trial=result(p);
 assert.equal((await a.read()).orderIds.length,12,'未采用试排保持独立');
 const adopted=await a.apply(trial);assert.equal(adopted.orderIds.length,13);
 const b=make(repo);assert.deepEqual(await b.init([{demoPlan:initialDemoPlan()}]),adopted,'旧对话不得覆盖当前计划');
 const rescheduled=await b.apply(result(await b.read(),'reschedule','b'));
 assert.equal(rescheduled.orderIds[0],'Z9900025');assert.ok(rescheduled.orderIds.includes('Z99100014'));assert.equal(rescheduled.orderIds.length,13);
 assert.deepEqual(await a.read(),rescheduled);
});
test('old and concurrent results cannot overwrite a newer shared plan',async()=>{
 const repo=repository(),a=factory()(repo),b=factory()(repo),p=await a.init([]);
 const outcomes=await Promise.allSettled([a.apply(result(p)),b.apply(result(p))]);
 assert.equal(outcomes.filter(x=>x.status==='fulfilled').length,1);assert.equal((await a.read()).revision,1);
 await assert.rejects(a.apply(result(p,'reschedule','c')),/更新|变化|过期|重复/);
});
test('MRP shares its saved reference but preserves the thirteen-order schedule',async()=>{
 const a=factory()(repository());const p=await a.init([]),next=await a.apply(result(p));
 const mrp=await a.apply(result(next,'mrp','c'));assert.equal(mrp.nativeResultId,next.nativeResultId);assert.deepEqual(mrp.orderIds,next.orderIds);
 await assert.rejects(a.apply(result(next,'mrp','d')),/更新|变化|过期|重复/);
 assert.equal((await a.read()).nativeMrpResultId,'c'.repeat(32));
});
test('migration preserves an adopted insertion rather than an old twelve-order conversation or an unadopted trial',async()=>{
 const p=initialDemoPlan(),insert=result(p),adopted=adoptDemoPlan(p,insert),old=adoptDemoPlan(p,result(p,'reschedule','b'));
 const a=factory()(repository()),migrated=await a.init([{id:'inserted',demoPlan:adopted,updatedAt:10,messages:[{role:'assistant',content:'已采用这次试排方案，后续查询将使用当前计划。',createdAt:10}]},{id:'old',demoPlan:old,updatedAt:999,messages:[{role:'assistant',content:'已采用这次试排方案，后续查询将使用当前计划。',createdAt:20}]}]);
 assert.equal(migrated.orderIds.length,13);
 const onlyTrial=await factory()(repository()).init([{demoPlan:p,messages:[{uiBlocks:[{spec:insert}]}]}]);assert.equal(onlyTrial.orderIds.length,12);
});
test('undo is tied to the exact adopted card and does not reset a newer plan to twelve',async()=>{
 const a=factory()(repository()),p=await a.init([]),insert=result(p);await a.apply(insert);
 const reorder=result(await a.read(),'reschedule','b');await a.apply(reorder);
 await assert.rejects(a.undo(insert),/更新|变化|过期/);
 const undone=await a.undo(reorder);assert.equal(undone.orderIds.length,13);assert.ok(undone.orderIds.includes('Z99100014'));assert.equal(undone.nativeResultId,insert.calculationId);assert.equal(undone.revision,3);
});
test('storage failure and cancellation leave the committed plan unchanged',async()=>{
 const repo=repository(),a=factory()(repo),p=await a.init([]),abort=new AbortController();abort.abort();
 await assert.rejects(a.apply(result(p),{signal:abort.signal}));assert.deepEqual(await a.read(),p);
 const broken={read:repo.read,transact:async()=>{throw Error('存储失败');}},b=factory()(broken);
 await assert.rejects(b.apply(result(p)),/存储失败/);assert.deepEqual(await a.read(),p);
});
test('a corrupted shared record fails closed instead of reverting to the initial twelve orders',async()=>{
 const a=factory()({read:async()=>({plan:{bad:true}}),transact:async f=>f({plan:{bad:true}})});
 await assert.rejects(a.init([]),/计划/);await assert.rejects(a.read(),/计划/);
});
test('reschedule and release candidates cannot roll back saved MRP or omit adopted orders',()=>{
 const p=adoptDemoPlan(initialDemoPlan(),result(initialDemoPlan()));p.nativeMrpResultId='e'.repeat(32);
 const stale=result(p,'reschedule','b');stale.proposedPlan.nativeMrpResultId=initialDemoPlan().nativeMrpResultId;
 assert.throws(()=>adoptDemoPlan(p,stale),/物料|核算|改变|引用/);
});
test('release preserves all thirteen orders and rejects repeats and locked mutations',async()=>{
 const a=factory()(repository()),p=await a.init([]),insert=await a.apply(result(p));
 const next={...clone(insert),revision:2,releaseStatus:'released',nativeReleaseId:'f'.repeat(32),releasedCalculationId:insert.nativeResultId};
 const release={...result(insert,'release'),proposedPlan:next,receipt:{sourceResultId:insert.nativeResultId}};
 const tampered=clone(release);tampered.proposedPlan.orderIds.pop();
 await assert.rejects(a.apply(tampered));
 assert.equal((await a.apply(release)).orderIds.length,13);
 await assert.rejects(a.apply(release));await assert.rejects(a.undo(result(p)));
 await assert.rejects(a.apply(result(next,'mrp')));
});
test('MRP cannot smuggle release metadata into an unreleased plan',()=>{
 const p=initialDemoPlan(),mrp=result(p,'mrp');mrp.proposedPlan.releasedCalculationId='f'.repeat(32);
 assert.throws(()=>adoptDemoPlan(p,mrp));
});
