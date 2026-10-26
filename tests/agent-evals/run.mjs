import fs from 'node:fs';import path from 'node:path';import assert from 'node:assert/strict';import {pathToFileURL} from 'node:url';
import {readSse} from '../../web-frontend/src/services/sse.js';
import {hasForbiddenAssertion} from './eval-text.mjs';
const base=process.env.BASE_URL||'http://localhost:18440';
const health=await fetch(base+'/api/agent/health').then(r=>r.json());
assert.equal(health.mode,'model','Real model evals require AGENT_MODE=model');
const allCases=fs.readFileSync(new URL('./cases.jsonl',import.meta.url),'utf8').trim().split('\n').map(JSON.parse);
const selectedIds=new Set((process.env.EVAL_CASE_IDS||'').split(',').filter(Boolean));
const cases=selectedIds.size ? allCases.filter(item=>selectedIds.has(item.id)) : allCases;
if(selectedIds.size)assert.equal(cases.length,selectedIds.size,'Unknown EVAL_CASE_IDS value');
const results=[];
for(const item of cases){
  const response=await fetch(base+'/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({conversationId:item.id,message:item.input,history:[]}),signal:AbortSignal.timeout(150000)});
  assert.equal(response.status,200);let text='',blocks=[],done;
  await readSse(response.body,(event,data)=>{if(event==='error')throw new Error(data.message);if(event==='delta')text+=data.text;if(event==='ui_payload')blocks.push(...data.blocks);if(event==='done')done=data;});
  assert.ok(done,'Stream must complete');
  if(item.toolCount!==undefined)assert.equal(done.toolCount,item.toolCount,item.id+': unexpected number of tool calls; '+text+'; '+JSON.stringify(done.toolCalls));
  if(item.noTool){assert.equal(done.toolCount,0,item.id+': '+JSON.stringify(done.toolCalls));assert.equal(blocks.length,0);assert.match(text,/只读|仅支持|尚未|暂不|不能|无法|已移除/);}
  else assert.ok(done.toolCount>=1,'Must call a tool');
  if(item.tool)assert.ok(done.toolCalls.some(t=>t.toolName===item.tool));
  if(item.failure){assert.equal(blocks.length,0);assert.ok(done.toolCalls.some(t=>!t.success));}
  if(item.viewType){
    const spec=blocks.find(b=>b.type==='aps-analysis')?.spec;assert.ok(spec);
    assert.equal(spec.viewType,item.viewType);assert.equal(spec.source,item.source);
    if(item.scheduleScope)assert.equal(spec.scheduleScope,item.scheduleScope);
    if(item.orderCount!==undefined)assert.equal(spec.detail.rows.length,item.orderCount);
    if(item.recordCount!==undefined)assert.equal(spec.chart.items.length,item.recordCount);
    for(const key of ['startDate','endDate'])if(item[key])assert.equal(done.toolCalls.find(t=>t.toolName===item.tool).arguments[key],item[key]);
    if(item.orderId)assert.ok(spec.chart.items.every(x=>x.orderId===item.orderId));
    if(item.version)assert.equal(spec.snapshotId,item.version);
    if(item.lineLoad){assert.equal(spec.lineLoad.available,true);assert.ok(spec.lineLoad.periods.some(r=>r.grain==='month'));}
    assert.ok(text.length<500,'Concise APS response');
  }
  for(const unexpected of item.mustNotContain||[])assert.ok(!hasForbiddenAssertion(text,unexpected),`Unexpected ${unexpected}: ${text}`);
  results.push({id:item.id,text,trace:done});
  if(item.chartType)assert.ok(blocks.some(b=>b.spec.chartType===item.chartType),'Expected chart type');
  if(item.blockType){const block=blocks.find(b=>b.type===item.blockType);assert.ok(block,'Expected production block');if(item.orderId)assert.equal(item.blockType==='order-dispatch'?block.spec.orderId:block.spec.focus.orderId,item.orderId);if(item.scope&&item.blockType!=='material-readiness')assert.equal(block.spec.focus.scope,item.scope);if(item.blockType==='order-dispatch'&&item.orderId)assert.equal(block.spec.orderRows.length,1);}
  if(item.blockType==='material-readiness'){
    const block=blocks.find(b=>b.type==='material-readiness');
    assert.ok(block.spec.model,'Material block must include a renderable model');
    assert.equal(block.spec.source,item.source);
    assert.equal(done.toolCalls.find(t=>t.toolName===item.tool).arguments.scope,item.scope);
    assert.ok(block.spec.model.rows.length>0,'Material block must include representative rows');
  }
  for(const expected of item.mustContain)assert.ok(text.includes(expected),`Missing ${expected}`);
  if(item.blockType==='production-plan'){
    assert.match(text,/示例|演示|合成/,'Must disclose synthetic data');
    assert.ok(text.length<500,'Production overview must remain concise');
  }
  console.log('PASS:',item.id,'toolCount='+done.toolCount,'textLength='+text.length);
}

const verificationDir=process.env.VERIFICATION_DIR
  ? pathToFileURL(path.resolve(process.env.VERIFICATION_DIR)+path.sep)
  : new URL("../../temp/verification/",import.meta.url);
fs.mkdirSync(verificationDir,{recursive:true});
fs.writeFileSync(new URL("agent-evals.json",verificationDir),JSON.stringify(results,null,2));
