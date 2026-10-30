import assert from 'node:assert/strict';
import fs from 'node:fs';
import {initialDemoPlan,adoptDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
const base=process.env.BASE_URL||'http://127.0.0.1:19440',records=[];
async function call(body,ok=true){const r=await fetch(base+'/api/agent/tools/interactive-demo',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const d=await r.json();assert.equal(d.success,ok,JSON.stringify(d));assert.equal(r.status,ok?200:400);records.push({body,result:d});return d.uiPayload?.[0]?.spec;}
const p=initialDemoPlan();
for(const [scenario,gap] of [['none',49029.22],['stock',43049.61],['supply',0],['late',0]]){
 const body={action:'mrp',orderId:'Z9900003',quantity:12500,scenario,plan:p};const a=await call(body),b=await call(body);assert.deepEqual(a,b);assert.equal(a.after.orders.length,12);assert.equal(a.simulation.purchaseNeed,gap);assert.equal(a.simulation.grossNeed,53049.61);assert.equal(a.simulation.stockUsed+a.simulation.transitUsed+a.simulation.purchaseNeed,a.simulation.grossNeed);
 for(const lot of a.after.supplyLots){assert.ok(a.after.orders.flatMap(o=>o.material.allocations).filter(x=>x.supplyOrderNo===lot.id).reduce((n,x)=>n+x.quantity,0)<=lot.quantity+0.000001);}
 for(const order of a.after.orders.filter(o=>o.scheduleStatus==='fullyScheduled'))assert.ok(order.planStart>=order.material.readyDate);
}
for(const body of [{action:'reschedule',orderId:'Z9900006'},{action:'mrp',orderId:'Z9900006',quantity:419},{action:'release'},{action:'mrp',orderId:'Z9999999'},{action:'mrp',orderId:'Z9900003',quantity:0},{action:'reschedule',orderId:'Z9900003',version:'261015002'}])await call({...body,plan:p},false);
const supplied=(await call({action:'mrp',orderId:'Z9900003',quantity:12500,scenario:'supply',plan:p})).proposedPlan;
const moved=await call({action:'reschedule',orderId:'Z9900003',position:1,plan:supplied});const target=moved.changes.find(x=>x.orderId==='Z9900003');assert.equal(target.afterEnd,'2026-10-06');assert.equal(target.beforeEnd,'2026-10-09');assert.ok(moved.changes.filter(x=>x.isLocked).every(x=>x.beforeEnd===x.afterEnd));
const late=await call({action:'mrp',orderId:'Z9900003',quantity:12500,scenario:'late',plan:adoptDemoPlan(supplied,moved)});assert.ok(late.changes.find(x=>x.orderId==='Z9900013').afterEnd>late.changes.find(x=>x.orderId==='Z9900013').beforeEnd);
const released=await call({action:'release',plan:adoptDemoPlan(supplied,moved)});for(const action of ['release','mrp','reschedule'])await call({action,orderId:'Z9900003',plan:released.proposedPlan},false);
fs.mkdirSync('output/verification',{recursive:true});fs.writeFileSync('output/verification/coupled12-interfaces.json',JSON.stringify({complete:true,requests:records.length,records},null,2));console.log('PASS deterministic twelve-order interfaces:',records.length);
