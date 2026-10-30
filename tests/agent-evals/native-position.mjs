import assert from 'node:assert/strict';
import {writeFileSync} from 'node:fs';
import {initialDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
import {readSse} from '../../web-frontend/src/services/sse.js';
const plan=initialDemoPlan(),response=await fetch('http://127.0.0.1:19440/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:'authored-position-three',message:'按已确认的信息将Z9900010调整到第3位，完成整批试排后告诉我结果。',history:[],demoPlan:plan,demoAction:{action:'reschedule',orderId:'Z9900010',position:3,quantity:0,scenario:'frozen',version:plan.demandVersion}}),signal:AbortSignal.timeout(180000)});
let text='',blocks=[],done;await readSse(response.body,(event,v)=>{if(event==='error')throw Error(v.message);if(event==='delta')text+=v.text||'';if(event==='ui_payload')blocks.push(...v.blocks);if(event==='done')done=v;});
const spec=blocks.find(b=>b.spec?.native)?.spec;const record={position:spec?.position,actualInputPosition:spec?.proposedPlan?.orderIds.indexOf('Z9900010')+1,text,done,blocks};
writeFileSync('output/verification/experience-fix/position.json',JSON.stringify(record,null,2));
assert.equal(done.modelInvoked,true);assert.equal(record.position,3);assert.equal(record.actualInputPosition,3);assert.ok(!text.includes('第1位'));console.log('PASS non-first position3');
