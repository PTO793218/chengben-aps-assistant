import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const demo = JSON.parse(fs.readFileSync(new URL('../../agent-backend/Data/production-demo.json', import.meta.url)));
const material = demo.materialReadiness;
const dispatch = demo.orderDispatch;
const ids = rows => rows.map(row => row.id ?? row.orderId ?? row.orderNo).sort();
const by = (rows, key) => new Map(rows.map(row => [row[key], row]));

test('all three demo scenes contain the same thirteen orders and simulated observation time', () => {
  assert.equal(demo.source, 'synthetic-demo');
  assert.equal(material.source, 'synthetic-demo');
  assert.equal(dispatch.source, 'synthetic-demo');
  assert.equal(demo.orders.length, 13);
  assert.deepEqual(ids(material.orders), ids(demo.orders));
  assert.deepEqual(ids(dispatch.orders), ids(demo.orders));
  assert.equal(material.asOf, demo.asOf);
  assert.equal(dispatch.asOf, demo.asOf);
});

test('each linked order preserves finished product, quantity, due date and plan timing', () => {
  const materialOrders = by(material.orders, 'orderId');
  const dispatchOrders = by(dispatch.orders, 'orderNo');
  const dispatchPlans = by(dispatch.plans, 'planGuid');
  for (const order of demo.orders) {
    const readiness = materialOrders.get(order.id);
    const current = dispatchOrders.get(order.id);
    assert.ok(readiness, `${order.id} missing material order`);
    assert.ok(current, `${order.id} missing dispatch order`);
    assert.equal(readiness.productName, order.product, `${order.id} material product`);
    assert.equal(readiness.orderQuantity, order.quantity, `${order.id} material quantity`);
    assert.equal(readiness.dueDate.slice(0, 10), order.dueDate, `${order.id} due date`);
    assert.equal(current.productName, order.product, `${order.id} dispatch product`);
    assert.equal(current.quantity, order.quantity, `${order.id} dispatch quantity`);
    const planTasks = demo.tasks.filter(task => task.orderId === order.id);
    const dispatchPlan = dispatchPlans.get(current.pdPlanGuid);
    if (planTasks.length === 0) {
      assert.equal(order.scheduleStatus, 'none');
      assert.equal(dispatchPlan, undefined, `${order.id} must not invent a plan`);
      continue;
    }
    assert.ok(dispatchPlan, `${order.id} missing dispatch plan`);
    const dates = planTasks.map(task => task.date).sort();
    assert.equal(current.planStart, dates[0], `${order.id} plan start`);
    assert.equal(current.planEnd, dates.at(-1), `${order.id} plan end`);
    assert.equal(dispatchPlan.planQuantity, order.quantity, `${order.id} plan quantity`);
  }
});

test('material evidence, current tasks and shared material links reconcile with the same order pool', () => {
  const known = new Set(ids(demo.orders));
  const statusByOrder = by(material.orderStatuses, 'orderId');
  const currentByPlan = Map.groupBy(dispatch.dispatchTasks, task => task.pdPlanGuid);
  const ordersByPlan = by(dispatch.orders, 'pdPlanGuid');
  for (const order of material.orders) {
    const rows = material.readinessResults.filter(row => row.orderId === order.orderId);
    const status = statusByOrder.get(order.orderId);
    assert.ok(status, `${order.orderId} missing readiness status`);
    assert.equal(status.readinessRowCount, rows.length);
    assert.equal(status.fullTagTRowCount, rows.filter(row => row.fullTag === 'T').length);
    if (status.status === 'status_not_obtained') assert.equal(rows.length, 0);
    if (status.status === 'full_tag_T') assert.ok(rows.length && rows.every(row => row.fullTag === 'T'));
    if (status.status === 'full_tag_F') assert.ok(rows.some(row => row.fullTag === 'F'));
    for (const row of rows) {
      if (row.fullTag === 'T') assert.ok(row.thisRemainNum >= row.materialNum, `${order.orderId} T inventory coverage`);
      if (row.fullTag === 'F' && row.lackNum !== undefined) {
        assert.equal(row.lackNum, Math.max(0, row.materialNum - row.thisRemainNum), `${order.orderId} shortage`);
        if (row.riskCode !== 'quality-blocked') assert.match(row.riskDetail, new RegExp(String(row.lackNum)), `${order.orderId} shortage explanation`);
      }
    }
  }
  for (const order of dispatch.orders) {
    const tasks = currentByPlan.get(order.pdPlanGuid) || [];
    const planOrder = demo.orders.find(row => row.id === order.orderNo);
    if (planOrder.scheduleStatus === 'none') assert.equal(tasks.length, 0);
    if (tasks.some(task => task.status === 'run' && task.taskQuantity > task.deliveryQuantity))
      assert.equal(planOrder.productionStatus, 'inProgress', `${order.orderNo} running task`);
    if (tasks.length && tasks.every(task => task.status === 'finish'))
      assert.equal(planOrder.productionStatus, 'completed', `${order.orderNo} completed tasks`);
    const plannedDates = demo.tasks.filter(task => task.orderId === order.orderNo).map(task => task.date).sort();
    for (const task of tasks) {
      assert.ok(plannedDates.length, `${order.orderNo} task without plan`);
      assert.ok(task.taskStart >= plannedDates[0] && task.taskEnd <= plannedDates.at(-1), `${order.orderNo} task dates`);
    }
  }
  const unmatched = dispatch.dispatchTasks.filter(task => !ordersByPlan.has(task.pdPlanGuid));
  assert.equal(unmatched.length, 1, 'one intentional source-unavailable task');
  const history = dispatch.latestTasks.find(task => task.taskNo === 'TA-Z9900006-HIST-01');
  assert.equal(history.taskStart, '2026-09-18');
  assert.equal(history.taskEnd, '2026-09-18');
  assert.equal(history.deliveryQuantity, history.taskQuantity);
  const bomByMaterial = Map.groupBy(material.productBomMaterials, row => row.materialNo);
  const orderByProduct = by(material.orders, 'productNo');
  for (const impact of material.materialImpactSummary) {
    const affected = [...new Set((bomByMaterial.get(impact.materialNo) || [])
      .map(row => orderByProduct.get(row.productNo)?.orderId).filter(Boolean))].sort();
    assert.deepEqual([...impact.orderIds].sort(), affected, `${impact.materialNo} impact IDs`);
    assert.equal(impact.orderCount, affected.length);
    assert.ok(affected.every(id => known.has(id)));
  }
  const shared = material.materialImpactSummary.find(row => row.materialNo === 'C.02.02.0010');
  assert.match(material.orders.find(row => row.scenarioId === 'S07-shared-material').riskDetail, new RegExp(`${shared.orderCount} 个订单`));
});
