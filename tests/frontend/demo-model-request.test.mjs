import test from 'node:test';
import assert from 'node:assert/strict';
import {requestDemo} from '../../web-frontend/src/services/demo-plan.js';

test('confirmed demo uses model chat stream and preserves confirmed inputs and evidence',async()=>{
  const original=globalThis.fetch;
  const confirmed={action:'mrp',orderId:'Z9900003',quantity:8000,scenario:'supply',position:1,version:'SIM-20261015',plan:{revision:2,orderIds:['Z9900005','Z9900013','Z9900003']}};
  globalThis.fetch=async(url,options)=>{
    assert.equal(url,'/api/agent/chat/stream');
    const request=JSON.parse(options.body);
    assert.equal(request.demoAction.quantity,8000);
    assert.equal(request.demoAction.orderId,'Z9900003');
    assert.deepEqual(request.demoPlan,confirmed.plan);
    const blocks=[{type:'interactive-demo',spec:{view:'mrp',orderId:'Z9900003',quantity:8000,scenario:'supply',purchaseNeed:1907.12}}];
    const frames=[['delta',{text:'模拟补料1907.12kg'}],['ui_payload',{blocks}],['done',{modelInvoked:true,mode:'model',toolCalls:[{toolName:'calculate_mrp_demo',success:true}]}]];
    return new Response(frames.map(([event,data])=>`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`).join(''),{headers:{'Content-Type':'text/event-stream'}});
  };
  try{const result=await requestDemo(confirmed);assert.equal(result.summary,'模拟补料1907.12kg');assert.equal(result.uiPayload[0].execution.modelInvoked,true);}
  finally{globalThis.fetch=original;}
});
