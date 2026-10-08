import assert from 'node:assert/strict';

const base = process.env.BASE_URL || 'http://localhost:19440';
const get = async path => {
  const response = await fetch(`${base}/api/agent/tools/${path}`, { signal: AbortSignal.timeout(15000) });
  assert.equal(response.status, 200, path);
  return response.json();
};

for (const [orderId, expectedQuantity, expectedPlan, expectedDispatch] of [
  ['Z9900001', 4200, '已排完整', 'dispatch_in_progress'],
  ['Z9900002', 12500, '已排完整', 'task_not_generated'],
  ['Z9900003', 50000, '尚未排入', 'task_not_generated'],
]) {
  const [schedule, material, dispatch] = await Promise.all([
    get(`aps-analysis?type=order&orderId=${orderId}`),
    get(`material-readiness?scope=order&orderId=${orderId}`),
    get(`order-dispatch-status?orderId=${orderId}`),
  ]);
  const planRow = schedule.uiPayload[0].spec.detail.rows[0];
  const materialRow = material.uiPayload[0].spec.model.rows[0];
  const dispatchRow = dispatch.uiPayload[0].spec.orderRows[0];
  assert.deepEqual([planRow.orderId, materialRow.orderId, dispatchRow.orderNo], [orderId, orderId, orderId]);
  assert.equal(planRow.quantity, expectedQuantity);
  assert.equal(dispatchRow.quantity, expectedQuantity);
  assert.equal(materialRow.productName, planRow.product);
  assert.equal(dispatchRow.product, planRow.product);
  assert.equal(planRow.scheduleStatus, expectedPlan);
  assert.equal(dispatchRow.statusKey, expectedDispatch);
  assert.equal(schedule.agentContext.asOf, material.agentContext.asOf);
  assert.equal(schedule.agentContext.asOf, dispatch.agentContext.asOf);
  assert.ok([schedule, material, dispatch].every(result => result.agentContext.source === 'synthetic-demo'));
  if (orderId === 'Z9900001') assert.equal(material.agentContext.readinessResultRowCount, 2);
  if (orderId === 'Z9900002') assert.ok(schedule.agentContext.scheduledTaskCount > 0, 'future plan must be visible');
  if (orderId === 'Z9900003') assert.match(dispatchRow.reason, /未排入计划/);
  console.log(`PASS: ${orderId} linked across plan, material and dispatch`);
}
