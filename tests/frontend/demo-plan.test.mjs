import test from 'node:test';
import assert from 'node:assert/strict';
// Historical schema3 messages retain their own state contract; new conversations are tested separately.
import {initialLegacyDemoPlan as initialDemoPlan,adoptDemoPlan,saveDemoMrp,resetDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
const candidate=(p,change={})=>({demandVersion:'261015001',baseRevision:p.revision,baseOrderIds:[...p.orderIds],baseState:structuredClone(p),proposedPlan:{...structuredClone(p),revision:p.revision+1,...change}});
test('adoption validates entire input, is immutable, and refuses repeat and ABA',()=>{
 const p=initialDemoPlan(); const c=candidate(p,{orderIds:[...p.orderIds.filter(id=>!['Z9900005','Z9900002','Z9900003','Z9900013'].includes(id)),'Z9900003','Z9900005','Z9900002','Z9900013']});
 const next=adoptDemoPlan(p,c); assert.equal(next.revision,1);assert.equal(p.revision,0);
 assert.throws(()=>adoptDemoPlan(next,c)); const reset=resetDemoPlan(next);assert.equal(reset.revision,2);assert.deepEqual(reset.orderIds,p.orderIds);assert.throws(()=>adoptDemoPlan(reset,c));
 const supplyChanged={...p,supplyScenario:'late'};assert.throws(()=>adoptDemoPlan(supplyChanged,c));
});
test('MRP adopts version demand and shared supply and advances revision',()=>{
 const p=initialDemoPlan();assert.equal(p.schemaVersion,3);assert.equal(p.supplyScenario,'stock');
 const c=candidate(p,{demandQuantities:{Z9900003:8000},supplyScenario:'supply'});c.view='mrp';c.orderId='Z9900003';c.quantity=8000;c.scenario='supply';c.after={orders:[{orderId:'Z9900003',quantity:8000}]};
 const next=saveDemoMrp(p,c);assert.equal(next.revision,1);assert.equal(next.demandQuantities.Z9900003,8000);assert.equal(next.supplyScenario,'supply');assert.deepEqual(p.demandQuantities,{});
 assert.equal(next.demandVersion,p.demandVersion);assert.throws(()=>saveDemoMrp(next,c));
});
test('released plans cannot adopt or reset, old schema cannot silently join new calculations',()=>{
 const p={...initialDemoPlan(),releaseStatus:'released',releasedCalculationId:'SIM-CALC-X'};assert.throws(()=>resetDemoPlan(p));assert.throws(()=>adoptDemoPlan(p,candidate(p)));
 const old={demandVersion:'261015001',revision:0,orderIds:[...p.orderIds],mrpInputs:[]};assert.throws(()=>adoptDemoPlan(old,candidate(p)));
});
