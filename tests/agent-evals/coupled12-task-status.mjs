import assert from 'node:assert/strict';
import fs from 'node:fs';
import {readSse} from '../../web-frontend/src/services/sse.js';
import {initialDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
const base=process.env.BASE_URL||'http://127.0.0.1:18440',records=[];
async function fixture(body){const r=await fetch(base+'/api/agent/tools/interactive-demo',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});assert.equal(r.status,200);return (await r.json()).uiPayload[0].spec.proposedPlan;}
for(const released of [false,true]){
 let p=initialDemoPlan();if(released){p=await fixture({action:'mrp',orderId:'Z9900003',quantity:12500,scenario:'supply',plan:p});p=await fixture({action:'release',plan:p});}
 const response=await fetch(base+'/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({message:'查询当前模拟版本的派工任务和计划来源。',history:[],demoPlan:p}),signal:AbortSignal.timeout(180000)});assert.equal(response.status,200);let blocks=[],done,text='';
 await readSse(response.body,(e,d)=>{if(e==='error')throw Error(d.message);if(e==='ui_payload')blocks.push(...d.blocks);if(e==='done')done=d;if(e==='delta')text+=d.text;});
 assert.equal(done.modelInvoked,true);assert.equal(done.toolCount,1);assert.equal(done.toolCalls[0].toolName,'get_order_dispatch_status');const s=blocks[0].spec;
 assert.equal(s.taskRows.filter(t=>t.locked).length,24);assert.ok(s.taskRows.filter(t=>t.locked).every(t=>t.statusKey==='run'));assert.equal(s.taskRows.filter(t=>!t.locked).length,released?12:0);assert.ok(s.taskRows.filter(t=>!t.locked).every(t=>t.statusKey==='created'));
 assert.equal(s.status.find(t=>t.key==='run').value,24);assert.equal(s.status.find(t=>t.key==='created').value,released?12:0);
 records.push({released,plan:p,text,blocks,done});console.log('PASS actual model task status:',released?'released:24run+12created':'unreleased:24run');
}
fs.writeFileSync('output/verification/coupled12-task-status.json',JSON.stringify({complete:true,records},null,2));
