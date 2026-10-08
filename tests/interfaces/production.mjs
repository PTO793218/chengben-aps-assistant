import assert from 'node:assert/strict';
const base=process.env.BASE_URL||'http://localhost:19440';
async function get(query=''){const r=await fetch(base+'/api/agent/tools/production'+query,{signal:AbortSignal.timeout(15000)});assert.equal(r.status,200);return r.json();}
const full=await get();assert.equal(full.agentContext.orderCount,12);assert.equal(full.uiPayload[0].type,'production-plan');
assert.equal(full.agentContext.pendingCount,10);assert.equal(full.uiPayload[0].spec.snapshot.source,'synthetic-demo');
const none=await get('?orderId=Z9900006');assert.equal(none.agentContext.tasks.length,0);assert.equal(none.agentContext.pendingCount,3);
const day=await get('?lineId=turn-1&date=2026-09-07');assert.equal(day.agentContext.tasks.length,2);
const unknown=await get('?orderId=missing');assert.equal(unknown.agentContext.orderCount,0);
assert.equal((await fetch(base+'/api/agent/tools/production?date=invalid')).status,400);
assert.equal((await fetch(base+'/api/agent/tools/production?lineId=invalid')).status,400);
console.log('PASS: production snapshot, counts, unscheduled, line/day, no matches and invalid parameters');
