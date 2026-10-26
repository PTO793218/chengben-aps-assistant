import assert from 'node:assert/strict';
import {writeFileSync} from 'node:fs';
import {initialDemoPlan,adoptDemoPlan,saveDemoMrp} from '../../web-frontend/src/services/demo-plan.js';
import {readSse} from '../../web-frontend/src/services/sse.js';

let plan=initialDemoPlan();const records=[];const base='http://127.0.0.1:18440';
async function call(message,demoAction){
 const start=Date.now();const response=await fetch(base+'/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:'native-demo-acceptance',message,history:[],demoPlan:plan,...(demoAction?{demoAction}: {})}),signal:AbortSignal.timeout(180000)});assert.equal(response.status,200);
 let text='',blocks=[],done;await readSse(response.body,(event,value)=>{if(event==='error')throw Error(value.message);if(event==='delta')text+=value.text||'';if(event==='ui_payload')blocks.push(...value.blocks);if(event==='done')done=value;});
 const record={message,elapsedMs:Date.now()-start,done,text,blocks};records.push(record);writeFileSync('output/verification/native-demo/model.json',JSON.stringify({status:'running',records},null,2));assert.equal(done?.modelInvoked,true,JSON.stringify(done));assert.ok(blocks.length>0,text);console.log('PASS',message,done.toolCalls?.map(x=>x.toolName).join(','));return blocks.find(x=>x.spec?.native)?.spec;
}
const action=(type,orderId='',position=1)=>({action:type,orderId,position,quantity:0,scenario:'frozen',version:plan.demandVersion});
try{
 let spec=await call('当前演示生产计划怎么样？');assert.equal(spec.orders.length,12);assert.equal(spec.calculationId,plan.nativeResultId);
 spec=await call('MRP计算演示 Z9900006');assert.equal(spec.prepare,true);assert.equal(spec.view,'mrp');
 spec=await call('请按本请求界面已确认的输入执行本地模拟MRP计算，调用对应演示工具并解释结果。',action('mrp','Z9900006'));assert.ok(spec.mrpRows.length>0);const basis=spec.mrpRows.find(r=>r.material==='C.02.02.0031');assert.equal(basis.stockUsed,500);assert.equal(basis.transitUsed,2000);assert.equal(basis.remaining,3520.7);assert.equal(spec.proposedPlan.nativeResultId,plan.nativeResultId);const originalPlan=plan.nativeResultId;plan=saveDemoMrp(plan,spec);assert.equal(plan.nativeResultId,originalPlan);
 spec=await call('查询演示订单 Z9900010 的物料及已保存MRP依据');assert.equal(spec.mrpResultId,plan.nativeMrpResultId);assert.equal(spec.calculationId,originalPlan);
 spec=await call('帮我把 Z9900010 调整到第1位试排');assert.equal(spec.prepare,true);assert.equal(spec.orderId,'Z9900010');
 spec=await call('请按本请求界面已确认的输入执行本地模拟整批试排，调用对应演示工具并解释结果。',action('reschedule','Z9900010'));assert.equal(spec.changes.length,12);assert.equal(spec.orders.length,12);assert.equal(spec.workspace.orders.length,12);assert.ok(spec.controlChangedOrders>=0);plan=adoptDemoPlan(plan,spec);assert.equal(plan.orderIds[0],'Z9900010');
 for(const message of ['查询演示订单 Z9900010 的排程','哪些演示订单有计划交期风险？','查询演示订单 Z9900010 的物料及已保存MRP依据','查询演示订单 Z9900026 的派工状态']){spec=await call(message);assert.equal(spec.calculationId,plan.nativeResultId);assert.equal(spec.mrpResultId,plan.nativeMrpResultId);}
 spec=await call('请准备模拟下达当前已展示的计划，先让我确认');assert.equal(spec.prepare,true);assert.equal(spec.view,'release');
 spec=await call('请按本请求界面已确认的输入执行本地模拟下达，调用对应演示工具并解释结果。',action('release'));assert.equal(spec.receipt.sourceResultId,plan.nativeResultId);assert.equal(spec.receipt.release.actualTaskRows,56);plan=adoptDemoPlan(plan,spec);assert.equal(plan.releaseStatus,'released');
 spec=await call('查询本批演示订单 Z9900026 的派工状态');assert.equal(spec.releaseStatus,'released');assert.equal(spec.tasks.length,6);
 writeFileSync('output/verification/native-demo/model.json',JSON.stringify({status:'PASS',plan,records},null,2));console.log('PASS ALL',records.length);
}catch(error){writeFileSync('output/verification/native-demo/model.json',JSON.stringify({status:'FAIL',error:String(error),plan,records},null,2));console.error(error);process.exitCode=1;}
