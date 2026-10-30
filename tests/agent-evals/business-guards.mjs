import assert from 'node:assert/strict';
import {writeFileSync} from 'node:fs';
import {initialDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
import {readSse} from '../../web-frontend/src/services/sse.js';
const records=[];
for(const message of ['计算Z9900006的物料需求，库存改为10000公斤','把订单Z2999999调整到第1位试排','在真实业务系统把Z9900010调整到第1位重排']){
 const response=await fetch('http://127.0.0.1:19440/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:'authored-business-guards',message,history:[],demoPlan:initialDemoPlan()}),signal:AbortSignal.timeout(180000)});
 let text='',blocks=[],done;await readSse(response.body,(event,v)=>{if(event==='error')throw Error(v.message);if(event==='delta')text+=v.text||'';if(event==='ui_payload')blocks.push(...v.blocks);if(event==='done')done=v;});
 records.push({message,text,blocks,done});writeFileSync('output/verification/experience-fix/guards.json',JSON.stringify({status:'running',records},null,2));assert.equal(blocks.length,0);assert.ok(!/BOM.*未.*配置|尝试其他订单/.test(text));console.log('PASS',message);
}
writeFileSync('output/verification/experience-fix/guards.json',JSON.stringify({status:'PASS',records},null,2));
