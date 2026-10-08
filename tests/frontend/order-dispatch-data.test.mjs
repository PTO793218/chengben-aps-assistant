import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';

const data = JSON.parse(fs.readFileSync(new URL('../../agent-backend/Data/production-demo.json', import.meta.url), 'utf8'));

test('order dispatch demo contains thirteen linked orders and consistent status counts', () => {
  const snapshot = data.orderDispatch;
  assert.ok(snapshot, 'the old planTaskAlignment snapshot must be replaced by orderDispatch');
  assert.equal(snapshot.source, 'synthetic-demo');
  assert.equal(snapshot.snapshotId, 'CB-ORDER-DISPATCH-DEMO-20260926');
  assert.equal(snapshot.orders.length, 13);
  assert.equal(snapshot.plans.length, 11);
  assert.equal(snapshot.dispatchTasks.length, 14);
  assert.equal(snapshot.latestTasks.length, 15);

  const planIds = new Set(snapshot.plans.map(plan => plan.planGuid));
  const currentTaskIds = new Set(snapshot.dispatchTasks.map(task => task.taskGuid));
  const linkedTasks = snapshot.dispatchTasks.filter(task => planIds.has(task.pdPlanGuid));
  const unmatchedTasks = snapshot.dispatchTasks.filter(task => !planIds.has(task.pdPlanGuid));
  assert.equal(linkedTasks.length, 13);
  assert.equal(unmatchedTasks.length, 1);
  assert.equal(unmatchedTasks[0].taskNo, 'TA260918999-01');

  const byPlan = new Map();
  for (const task of linkedTasks) {
    const rows = byPlan.get(task.pdPlanGuid) || [];
    rows.push(task);
    byPlan.set(task.pdPlanGuid, rows);
  }
  const derived = snapshot.orders.map(order => {
    const tasks = byPlan.get(order.pdPlanGuid) || [];
    const inProgress = tasks.some(task => task.status === 'run' && task.taskQuantity - task.deliveryQuantity > 0);
    const status = inProgress
      ? 'dispatch_in_progress'
      : tasks.length && tasks.every(task => task.status === 'finish')
        ? 'dispatch_completed'
        : tasks.length && tasks.every(task => task.status === 'close')
          ? 'dispatch_closed'
          : tasks.length === 0
            ? 'task_not_generated'
            : 'dispatch_status_unknown';
    return { orderNo: order.orderNo, status };
  });
  const counts = Object.groupBy(derived, row => row.status);
  assert.equal(counts.dispatch_in_progress.length, 8);
  assert.equal(counts.dispatch_completed.length, 1);
  assert.equal(counts.dispatch_closed.length, 1);
  assert.equal(counts.task_not_generated.length, 3);
  assert.equal(counts.dispatch_status_unknown, undefined);
  assert.deepEqual(
    derived.find(row => row.orderNo === 'Z9900001'),
    { orderNo: 'Z9900001', status: 'dispatch_in_progress' },
  );
  assert.deepEqual(
    derived.find(row => row.orderNo === 'Z9900002'),
    { orderNo: 'Z9900002', status: 'task_not_generated' },
  );
  assert.deepEqual(
    derived.find(row => row.orderNo === 'Z9900006'),
    { orderNo: 'Z9900006', status: 'task_not_generated' },
  );
  assert.equal(currentTaskIds.size, snapshot.dispatchTasks.length);
  assert.ok(snapshot.latestTasks.some(task => task.taskNo === 'TA-Z9900006-HIST-01'));
});
