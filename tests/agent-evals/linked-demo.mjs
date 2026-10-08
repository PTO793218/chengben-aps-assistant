import assert from 'node:assert/strict';
import { readSse } from '../../web-frontend/src/services/sse.js';

const base = process.env.BASE_URL || 'http://localhost:19440';
const health = await fetch(`${base}/api/agent/health`).then(response => response.json());
assert.equal(health.mode, 'model');

for (const [message, toolName, blockType] of [
  ['查询演示订单 Z9900001 的排程。', 'get_order_schedule', 'aps-analysis'],
  ['演示订单 Z9900001 的物料齐套状态如何？', 'get_material_readiness', 'material-readiness'],
  ['演示订单 Z9900001 派工了吗？', 'get_order_dispatch_status', 'order-dispatch'],
]) {
  const response = await fetch(`${base}/api/agent/chat/stream`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ conversationId: crypto.randomUUID(), message, history: [] }),
    signal: AbortSignal.timeout(150000),
  });
  assert.equal(response.status, 200);
  let answer = '';
  let blocks = [];
  let done;
  await readSse(response.body, (event, data) => {
    if (event === 'error') throw new Error(data.message);
    if (event === 'delta') answer += data.text;
    if (event === 'ui_payload') blocks.push(...data.blocks);
    if (event === 'done') done = data;
  });
  assert.equal(done?.toolCount, 1, `${message}: one business tool`);
  assert.equal(done.toolCalls[0].toolName, toolName);
  const block = blocks.find(item => item.type === blockType);
  assert.ok(block, `${message}: expected ${blockType} block`);
  assert.match(answer, /Z9900001/);
  assert.match(answer, /演示|合成|非实时/);
  if (blockType === 'aps-analysis') assert.equal(block.spec.detail.rows[0].orderId, 'Z9900001');
  if (blockType === 'material-readiness') assert.equal(block.spec.model.rows[0].orderId, 'Z9900001');
  if (blockType === 'order-dispatch') assert.equal(block.spec.orderRows[0].orderNo, 'Z9900001');
  console.log(`PASS: ${toolName} routed one linked demo order`);
}
