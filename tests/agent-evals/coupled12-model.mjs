import assert from 'node:assert/strict';
import fs from 'node:fs';
import {readSse} from '../../web-frontend/src/services/sse.js';
import {initialDemoPlan,adoptDemoPlan,saveDemoMrp} from '../../web-frontend/src/services/demo-plan.js';
const base=process.env.BASE_URL||'http://127.0.0.1:18440',records=[];
fs.mkdirSync('output/verification',{recursive:true});
const record=()=>fs.writeFileSync('output/verification/coupled12-model.json',JSON.stringify({complete:false,records},null,2));
async function ask(message,plan,tool,{action,type}={}){
 const response=await fetch(base+'/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:crypto.randomUUID(),message,history:[],demoPlan:plan,demoAction:action}),signal:AbortSignal.timeout(180000)});
 assert.equal(response.status,200);let text='',blocks=[],done;
 await readSse(response.body,(e,d)=>{if(e==='error')throw Error(d.message);if(e==='delta')text+=d.text;if(e==='ui_payload')blocks.push(...d.blocks);if(e==='done')done=d;});
 records.push({message,plan,action,text,blocks,done});record();assert.equal(done.modelInvoked,true);assert.equal(done.toolCount,1,JSON.stringify(done.toolCalls));assert.equal(done.toolCalls[0].toolName,tool);assert.equal(done.toolCalls[0].success,true);assert.equal(blocks.length,1);if(type)assert.equal(blocks[0].type,type);
 assert.equal(blocks[0].spec.demandVersion,'261015001');console.log('PASS actual model:',records.length,tool);return blocks[0].spec;
}
let p=initialDemoPlan();
const initial=await ask('当前模拟生产计划怎么样？',p,'get_plan_overview');assert.equal(initial.detail.rows.length,12);assert.equal(initial.simulation.fullyScheduled,8);assert.equal(initial.simulation.unscheduled,4);assert.equal(initial.simulation.purchaseNeed,43049.61);
const material=await ask('查询当前模拟版本所有订单的物料需求及齐套。',p,'get_material_readiness');assert.equal(material.model.rows.length,12);assert.equal(material.calculationId,initial.calculationId);assert.equal(material.simulation.reservedStock,4020.39);
const dispatchBefore=await ask('查询当前模拟版本的派工和任务状态。',p,'get_order_dispatch_status');assert.equal(dispatchBefore.orderRows.length,12);assert.equal(dispatchBefore.taskRows.length,24);assert.ok(dispatchBefore.taskRows.every(t=>t.locked));
const mrpPrepare=await ask('MRP计算演示 Z9900003，需求数量12500，确认到货的供应场景。',p,'prepare_mrp_demo');assert.equal(mrpPrepare.orders.length,12);assert.equal(mrpPrepare.orderId,'Z9900003');
const supply=await ask('请按本请求界面已确认输入执行本地模拟MRP计算。',p,'calculate_mrp_demo',{action:{action:'mrp',orderId:'Z9900003',quantity:12500,scenario:'supply',version:'261015001'}});
assert.equal(supply.simulation.fullyScheduled,12);assert.equal(supply.simulation.purchaseNeed,0);assert.equal(supply.after.orders.find(o=>o.orderId==='Z9900003').planEnd,'2026-10-09');p=saveDemoMrp(p,supply);
const reorder=await ask('把演示订单 Z9900003 调整到待排队列第1位试排，先给我确认。',p,'prepare_reschedule_demo');assert.equal(reorder.orders.length,12);
const candidate=await ask('请按本请求界面已确认输入执行本地模拟整批试排。',p,'simulate_reschedule_demo',{action:{action:'reschedule',orderId:'Z9900003',position:1,version:'261015001'}});
assert.equal(candidate.after.orders.length,12);const urgent=candidate.changes.find(o=>o.orderId==='Z9900003');assert.equal(urgent.beforeEnd,'2026-10-09');assert.equal(urgent.afterEnd,'2026-10-06');assert.equal(urgent.afterRisk,false);assert.equal(urgent.afterMaterial.stockUsed,5979.61);
for(const row of candidate.changes.filter(o=>o.isLocked)){assert.equal(row.beforeStart,row.afterStart);assert.equal(row.beforeEnd,row.afterEnd);assert.deepEqual(row.beforeMaterial,row.afterMaterial);}
assert.equal(candidate.changes.find(o=>o.orderId==='Z9900002').afterEnd,'2026-10-09');p=adoptDemoPlan(p,candidate);
const overview=await ask('查询当前模拟版本的生产计划总览。',p,'get_plan_overview');assert.equal(overview.detail.rows.length,12);assert.equal(overview.calculationId,candidate.calculationId);
const risk=await ask('哪些演示订单有计划交期风险？',p,'get_delivery_risks');assert.equal(risk.detail.rows.length,10);assert.ok(!risk.detail.rows.some(o=>o.orderId==='Z9900003'));
const order=await ask('查询演示订单 Z9900003 的排程。',p,'get_order_schedule');assert.equal(order.detail.rows[0].plannedEnd,'2026-10-06');assert.equal(order.calculationId,candidate.calculationId);
const afterMaterial=await ask('查询 Z9900003 已保存的MRP结果和物料分配。',p,'get_material_readiness');assert.equal(afterMaterial.workspace.orders[0].material.stockUsed,5979.61);assert.equal(afterMaterial.calculationId,candidate.calculationId);
await ask('请准备模拟下达当前版本，先让我确认风险。',p,'prepare_release_demo');
const released=await ask('请按本请求界面已确认输入执行本地模拟下达。',p,'release_demo_plan',{action:{action:'release',orderId:'',version:'261015001'}});p=adoptDemoPlan(p,released);
const tasks=await ask('查看当前模拟版本的所有派工任务及计划来源。',p,'get_order_dispatch_status');assert.equal(tasks.taskRows.length,36);assert.equal(tasks.taskRows.filter(t=>t.locked).length,24);assert.equal(tasks.taskRows.filter(t=>!t.locked).length,12);assert.ok(tasks.taskRows.filter(t=>!t.locked).every(t=>t.sourceCalculationId===candidate.calculationId&&t.sourcePlanRecordIds.length>0));assert.deepEqual(tasks.taskRows.filter(t=>t.locked).map(t=>({id:t.id,ids:t.sourcePlanRecordIds})),dispatchBefore.taskRows.map(t=>({id:t.id,ids:t.sourcePlanRecordIds})));
fs.writeFileSync('output/verification/coupled12-model.json',JSON.stringify({complete:true,actualModel:true,requests:records.length,records},null,2));
console.log('PASS: 14 actual-model requests; twelve-order story, locked reservations, shared allocation, cross-query facts and task provenance.');
