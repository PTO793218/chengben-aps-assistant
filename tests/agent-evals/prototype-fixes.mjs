import assert from 'node:assert/strict';
import fs from 'node:fs';
import { readSse } from '../../web-frontend/src/services/sse.js';

const base = process.env.BASE_URL || 'http://localhost:18443';
const health = await fetch(`${base}/api/agent/health`).then(r => r.json());
assert.equal(health.mode, 'model', 'This eval requires the actual model');
const results = [];
const history = [];
const conversationId = crypto.randomUUID();
async function query(message) {
  const response = await fetch(`${base}/api/agent/chat/stream`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ conversationId, message, history }), signal: AbortSignal.timeout(150000),
  });
  assert.equal(response.status, 200);
  let text = '', blocks = [], done;
  await readSse(response.body, (event, data) => {
    if (event === 'error') throw new Error(data.message);
    if (event === 'delta') text += data.text;
    if (event === 'ui_payload') blocks.push(...data.blocks);
    if (event === 'done') done = data;
  });
  assert.ok(done);
  results.push({ message, text, done });
  history.push({ role: 'user', content: message }, { role: 'assistant', content: text });
  fs.mkdirSync(new URL('../../temp/verification/', import.meta.url), { recursive: true });
  fs.writeFileSync(new URL('../../temp/verification/prototype-fixes-model.json', import.meta.url), JSON.stringify(results, null, 2));
  return { text, blocks, done };
}

const scope = await query('只说明原型能力，不查询明细：计划、物料、派工是否共用同一批演示订单，能否分别按同一个演示订单号核对？');
assert.match(scope.text, /同一批|共用|已关联|同一.*订单/);
assert.match(scope.text, /演示|合成|受控/);
assert.doesNotMatch(scope.text, /尚未关联|未关联|不能.*关联|各自独立的.*样本/);
assert.equal(scope.done.toolCount, 0);
console.log('PASS: linked demo capability is described accurately');

for (const [message, tool, type, id] of [
  ['查询演示订单 Z9900006 的排程。', 'get_order_schedule', 'aps-analysis', 'Z9900006'],
  ['演示订单 Z9900006 的物料齐套状态如何？', 'get_material_readiness', 'material-readiness', 'Z9900006'],
  ['演示订单 Z9900006 派工了吗？', 'get_order_dispatch_status', 'order-dispatch', 'Z9900006'],
]) {
  const result = await query(message);
  assert.equal(result.done.toolCount, 1);
  assert.equal(result.done.toolCalls[0].toolName, tool);
  const block = result.blocks.find(b => b.type === type);
  assert.ok(block);
  assert.equal(block.spec.source, 'synthetic-demo');
  const rows = type === 'aps-analysis' ? block.spec.detail.rows : type === 'material-readiness' ? block.spec.model.rows : block.spec.orderRows;
  assert.ok(rows.length > 0);
  assert.ok(rows.every(row => (row.orderId || row.orderNo) === id));
  assert.match(result.text, /演示|合成|非实时/);
  console.log(`PASS: ${tool} retains the requested demo order within one conversation`);
}

for (const topic of ['物料齐套', '订单派工']) {
  const unavailable = await query(`现在切换到真实订单 Z9900006，查询它在真实业务库里的${topic}，不能用演示数据。`);
  assert.equal(unavailable.done.toolCount, 0);
  assert.equal(unavailable.blocks.length, 0);
  assert.match(unavailable.text, /未接入|尚未|暂不|不支持|不能|无法/);
  console.log(`PASS: real ${topic} query does not silently switch to a demo`);
}
