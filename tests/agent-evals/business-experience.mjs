import assert from 'node:assert/strict';
import {writeFileSync,mkdirSync} from 'node:fs';
import {initialDemoPlan,adoptDemoPlan,saveDemoMrp} from '../../web-frontend/src/services/demo-plan.js';
import {readSse} from '../../web-frontend/src/services/sse.js';
let plan=initialDemoPlan();const records=[],out='output/verification/experience-fix';mkdirSync(out,{recursive:true});
const save=(status,error)=>writeFileSync(out+'/model.json',JSON.stringify({status,error,plan,records},null,2));
async function call(message,expected,demoAction,history=[]){
 const started=Date.now(),response=await fetch('http://127.0.0.1:19440/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:'authored-business-experience',message,history,demoPlan:plan,...(demoAction?{demoAction}:{})}),signal:AbortSignal.timeout(180000)});assert.equal(response.status,200);
 let text='',blocks=[],done;await readSse(response.body,(event,v)=>{if(event==='error')throw Error(v.message);if(event==='delta')text+=v.text||'';if(event==='ui_payload')blocks.push(...v.blocks);if(event==='done')done=v;});
 const record={message,text,blocks,done,elapsedMs:Date.now()-started};records.push(record);save('running');
 assert.equal(done?.modelInvoked,true);assert.ok(done.toolCalls?.length);const spec=blocks.find(b=>b.spec?.native)?.spec;
 assert.ok(spec,text);assert.equal(spec.view,expected);assert.equal(spec.presentationVersion,2);
 assert.ok(!/大模型|确定性|回滚|Submit|SIM-|GUID|本地|隔离|冻结|演示|模拟|native|需求版本|只使用本次/.test(text),'开发说明泄漏: '+text);
 assert.equal(done.toolCalls.filter(t=>t.success).length,1,'当前主问题不得追加多项查询');
 if(spec.prepare)assert.ok(!/已把|已将|试排完成|下发完成|提交成功|结果已生成|预览已生成/.test(text),'准备阶段不能宣称已执行');
 assert.ok(!/[a-f0-9]{32}/.test(text),'内部引用泄漏');assert.ok(text.length<380,'回复过长: '+text.length);
 assert.equal(spec.calculationId,expected==='reschedule'&&!spec.prepare?spec.proposedPlan.nativeResultId:plan.nativeResultId);
 if(expected!=='mrp'||spec.prepare)assert.equal(spec.mrpResultId,plan.nativeMrpResultId);
 console.log('PASS',records.length,expected,text);return spec;
}
const action=(action,orderId='',position=1)=>({action,orderId,position,quantity:0,scenario:'frozen',version:plan.demandVersion});
try{
 let spec=await call('当前生产计划怎么样？','overview');assert.equal(spec.orders.length,12);
 spec=await call('Z9900010怎么安排？','order');assert.equal(spec.orders.length,1);assert.equal(spec.orderId,'Z9900010');assert.ok(!/已(?:提供|展示).*(?:负载|理论工时|年月日)/.test(records.at(-1).text));
 spec=await call('哪些订单有交期风险？','risk',undefined,[{role:'user',content:'以前风险怎样？'},{role:'assistant',content:'旧回答：本地隔离演示SIM-OLD，Z9900019计划结束2027-05-07。负载率及年月日切换已展示。'}]);
 assert.ok(!records.at(-1).text.includes('2027-05-07'));const allowed=new Set(spec.orders.flatMap(o=>[o.planStart,o.planEnd,o.dueDate]));for(const d of records.at(-1).text.match(/20\d{2}-\d{2}-\d{2}/g)||[])assert.ok(allowed.has(d),'未查询到的日期 '+d);
 spec=await call('计算Z9900006的物料需求','mrp');assert.equal(spec.prepare,true);
 spec=await call('按已确认的信息计算物料需求，完成后告诉我结果。','mrp',action('mrp','Z9900006'));
 const leaf=spec.mrpRows.find(r=>r.material==='C.02.02.0031');assert.deepEqual([leaf.beforeOffset,leaf.stockUsed,leaf.transitUsed,leaf.remaining],[6020.7,500,2000,3520.7]);
 const baseline=plan.nativeResultId;plan=saveDemoMrp(plan,spec);assert.equal(plan.nativeResultId,baseline);
 spec=await call('核对订单Z9900006的物料和已保存MRP依据','material');assert.equal(spec.orders.length,1);assert.equal(spec.mrpResultId,plan.nativeMrpResultId);
 spec=await call('把Z9900010调整到第1位试排，先让我确认','reschedule');assert.equal(spec.prepare,true);assert.equal(spec.orderId,'Z9900010');
 spec=await call('按已确认的信息执行整批试排，完成后告诉我结果。','reschedule',action('reschedule','Z9900010'));
 assert.equal(spec.changes.length,12);assert.equal(spec.controlChangedOrders>=0,true);plan=adoptDemoPlan(plan,spec);assert.equal(plan.orderIds[0],'Z9900010');
 for(const [question,view] of [['查询订单Z9900010的排程','order'],['现在这批订单的交期风险怎样？','risk'],['核对订单Z9900006的物料和已保存MRP依据','material'],['Z9900026的派工情况','dispatch']]){
  spec=await call(question,view);if(view==='dispatch'){assert.equal(spec.tasks.length,6);assert.ok(spec.tasks.every(t=>t.orderIds.includes('Z9900026')));}
 }
 spec=await call('准备下发当前计划，先让我确认','release');assert.equal(spec.prepare,true);
 spec=await call('按已确认的信息下发当前计划，完成后告诉我结果。','release',action('release'));
 assert.equal(spec.receipt.sourceResultId,plan.nativeResultId);assert.equal(spec.receipt.release.actualTaskRows,56);plan=adoptDemoPlan(plan,spec);assert.equal(plan.releaseStatus,'released');
 spec=await call('再看看Z9900026的派工情况','dispatch');assert.equal(spec.tasks.length,6);assert.equal(spec.releaseStatus,'released');
 save('PASS');console.log('PASS ALL',records.length);
}catch(e){save('FAIL',String(e));console.error(e);process.exitCode=1;}
