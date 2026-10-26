import assert from 'node:assert/strict';
import {writeFileSync} from 'node:fs';
import {readSse} from '../../web-frontend/src/services/sse.js';
import {initialDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
const records=[];
for(const message of ['哪些演示订单有计划交期风险？','查询演示物料 C.02.02.0031 的需求和库存、在途抵扣']){
 const response=await fetch('http://127.0.0.1:18440/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({message,history:[{role:'user',content:'旧版本的计划风险是什么？'},{role:'assistant',content:'旧版本Z9900019计划结束2027-05-07，交期2026-06-04；这是旧回答，未重新查询。'}],demoPlan:initialDemoPlan()}),signal:AbortSignal.timeout(180000)});
 let text='',blocks=[],done;await readSse(response.body,(event,value)=>{if(event==='error')throw Error(value.message);if(event==='delta')text+=value.text||'';if(event==='ui_payload')blocks.push(...value.blocks);if(event==='done')done=value;});
 const spec=blocks.find(x=>x.spec?.native)?.spec;records.push({message,text,blocks,done});writeFileSync('output/verification/native-demo/model-current-facts.json',JSON.stringify({status:'running',records},null,2));
 assert.equal(done.modelInvoked,true);assert.ok(spec);assert.ok(done.toolCalls.length>0);assert.ok(!text.includes('2027-05-07')&&!text.includes('2026-06-04'),'不得沿用错误旧日期');
 if(message.includes('物料')){assert.equal(spec.orders.length,1);assert.equal(spec.orders[0].orderId,'Z9900006');assert.ok(spec.mrpRows.every(r=>r.material==='C.02.02.0031'));assert.ok(!text.includes('C.02.02.0010'));}
 else{const allowed=new Set(spec.orders.flatMap(o=>[o.planStart,o.planEnd,o.dueDate]));for(const day of text.match(/20\d{2}-\d{2}-\d{2}/g)||[])assert.ok(allowed.has(day),`未知日期 ${day}`);}
 console.log('PASS',message);
}
writeFileSync('output/verification/native-demo/model-current-facts.json',JSON.stringify({status:'PASS',records},null,2));
