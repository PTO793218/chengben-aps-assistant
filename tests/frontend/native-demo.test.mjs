import test from 'node:test';
import assert from 'node:assert/strict';
import {initialDemoPlan,adoptDemoPlan,saveDemoMrp,resetDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
import * as presentation from '../../web-frontend/src/services/business-presentation.js';

const clone=x=>structuredClone(x);
test('previous native dataset remains readable but cannot offer executable cards',()=>{
 assert.equal(typeof presentation.currentBusinessBlock,'function');
 const old={type:'interactive-demo',spec:{native:true,demandVersion:'SIM-261016-12A',view:'reschedule',prepare:true,orders:[{orderId:'Z9900010'}],tasks:[{task_bill_no:'existing'}],proposedPlan:{}}};
 const shown=presentation.currentBusinessBlock(old,initialDemoPlan().demandVersion);
 assert.equal(shown.spec.archived,true);assert.equal(shown.spec.prepare,false);assert.equal(shown.spec.proposedPlan,undefined);
 assert.deepEqual(shown.spec.orders,old.spec.orders);assert.deepEqual(shown.spec.tasks,old.spec.tasks);
 const current={...old,spec:{...old.spec,demandVersion:initialDemoPlan().demandVersion}};
 assert.equal(presentation.currentBusinessBlock(current,initialDemoPlan().demandVersion),current);
});
function candidate(p,view){const proposedPlan={...clone(p),revision:p.revision+1};if(view==='reschedule'){proposedPlan.orderIds=[...p.orderIds].reverse();proposedPlan.nativeResultId='a'.repeat(32);}if(view==='mrp')proposedPlan.nativeMrpResultId='b'.repeat(32);return{native:true,view,demandVersion:p.demandVersion,baseState:clone(p),baseRevision:p.revision,proposedPlan,calculationId:proposedPlan.nativeResultId,mrpResultId:proposedPlan.nativeMrpResultId};}

test('new conversations use native twelve-order source with frozen business inputs',()=>{
 const p=initialDemoPlan();assert.equal(p.schemaVersion,4);assert.equal(p.orderIds.length,12);assert.ok(p.orderIds.includes('Z9900010'));assert.equal(p.supplyScenario,'frozen');assert.match(p.nativeResultId,/^[a-f0-9]{32}$/);
});
test('adopt references the displayed calculation and stale result cannot overwrite it',()=>{
 const p=initialDemoPlan(),c=candidate(p,'reschedule'),next=adoptDemoPlan(p,c);assert.equal(next.nativeResultId,c.calculationId);assert.throws(()=>adoptDemoPlan(next,c),/变化|重复|过期/);
 const tampered=clone(c);tampered.proposedPlan.nativeResultId='c'.repeat(32);assert.throws(()=>adoptDemoPlan(p,tampered),/结果|计算/);
});
test('saving independent MRP leaves schedule, priority and supply unchanged',()=>{
 const p=initialDemoPlan(),c=candidate(p,'mrp'),next=saveDemoMrp(p,c);assert.equal(next.nativeResultId,p.nativeResultId);assert.deepEqual(next.orderIds,p.orderIds);assert.equal(next.supplyScenario,'frozen');assert.equal(next.nativeMrpResultId,'b'.repeat(32));
 c.proposedPlan.orderIds.reverse();assert.throws(()=>saveDemoMrp(p,c),/MRP|顺序|计划/);
});
test('restoring references original saved result without rerunning or discarding independent MRP',()=>{
 const initial=initialDemoPlan(),p=adoptDemoPlan(initial,candidate(initial,'reschedule'));p.nativeMrpResultId='b'.repeat(32);const restored=resetDemoPlan(p);assert.equal(restored.nativeResultId,initial.nativeResultId);assert.equal(restored.nativeMrpResultId,p.nativeMrpResultId);assert.deepEqual(restored.orderIds,initial.orderIds);
});
