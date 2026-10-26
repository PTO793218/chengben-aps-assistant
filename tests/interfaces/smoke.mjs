import assert from 'node:assert/strict';
const base=process.env.BASE_URL||'http://localhost:18440';
async function get(path){const response=await fetch(base+path,{signal:AbortSignal.timeout(15000)});assert.equal(response.status,200,path);return response;}
assert.match(await (await get('/')).text(),/诚本 APS 助手/);
const health=await (await get('/api/agent/health')).json();
assert.equal(health.status,'ok');
assert.equal((await (await get('/api/speech-health')).json()).status,'ok');
for(const type of ['bar','line','pie']){
  const tool=await (await get('/api/agent/tools/sample?chartType='+type)).json();
  assert.equal(tool.success,true);assert.equal(tool.agentContext.total,320);assert.equal(tool.uiPayload[0].spec.chartType,type);
}
assert.equal((await fetch(base+'/api/agent/tools/sample?chartType=invalid')).status,400);
assert.equal((await fetch(base+'/api/agent/chat/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({message:''})})).status,400);
assert.equal((await fetch(base+'/api/speech/asr/stream')).status,400);
assert.equal((await fetch(base+'/api/speech/tts/stream',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text:''})})).status,400);
console.log('PASS: frontend, health, tool contracts, invalid requests, speech routes. Agent mode:',health.mode);
