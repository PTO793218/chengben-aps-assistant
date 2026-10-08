import test from 'node:test';
import assert from 'node:assert/strict';
import { buildTaskStatusByLine, orderDispatchFilterLabel, selectOrderDispatchRows } from '../../web-frontend/src/services/order-dispatch.js';

const orders = [
  { rowType: 'order', orderNo: 'O1', statusKey: 'dispatch_in_progress', status: '派工在制', line: 'L1' },
  { rowType: 'order', orderNo: 'O2', statusKey: 'task_not_generated', status: '未形成当前任务', line: 'L2' },
];
const tasks = [
  { rowType: 'task', number: 'T1', statusKey: 'run', status: '运行中', line: 'L1' },
  { rowType: 'task', number: 'T2', statusKey: 'run', status: '运行中', line: 'L1' },
  { rowType: 'task', number: 'T3', statusKey: 'finish', status: '已完成', line: 'L2' },
];
const spec = { detailGroups: {
  allOrders: orders,
  dispatchInProgress: [orders[0]],
  taskNotGenerated: [orders[1]],
  allTasks: tasks,
  runTasks: tasks.slice(0, 2),
  finishTasks: [tasks[2]],
  unmatchedTask: [],
} };

test('selects order rows and task rows from the same order-dispatch payload', () => {
  assert.deepEqual(selectOrderDispatchRows(spec, 'all-orders'), orders);
  assert.deepEqual(selectOrderDispatchRows(spec, 'dispatch-in-progress'), [orders[0]]);
  assert.deepEqual(selectOrderDispatchRows(spec, 'task-not-generated'), [orders[1]]);
  assert.deepEqual(selectOrderDispatchRows(spec, 'run'), tasks.slice(0, 2));
  assert.equal(orderDispatchFilterLabel('unmatched-task'), '任务来源未取得');
});

test('groups current task status by line without counting order rows', () => {
  assert.deepEqual(buildTaskStatusByLine(spec), [
    { line: 'L1', run: 2, finish: 0, close: 0, total: 2 },
    { line: 'L2', run: 0, finish: 1, close: 0, total: 1 },
  ]);
});

test('chart drilldown selects both task status and clicked line', () => {
  const otherLine = { rowType: 'task', number: 'T4', statusKey: 'run', line: 'L2' };
  const data = { detailGroups: { ...spec.detailGroups, runTasks: [...tasks.slice(0, 2), otherLine] } };
  assert.deepEqual(selectOrderDispatchRows(data, 'run', 'L1'), tasks.slice(0, 2));
  assert.deepEqual(selectOrderDispatchRows(data, 'run', 'L2'), [otherLine]);
  assert.deepEqual(selectOrderDispatchRows(data, 'run', 'L3'), []);
  assert.equal(selectOrderDispatchRows(data, 'run').length, 3);
});
