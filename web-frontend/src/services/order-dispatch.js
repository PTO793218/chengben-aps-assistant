const groupKeys = {
  'all-orders': 'allOrders',
  'dispatch-in-progress': 'dispatchInProgress',
  'dispatch-completed': 'dispatchCompleted',
  'dispatch-closed': 'dispatchClosed',
  'task-not-generated': 'taskNotGenerated',
  unknown: 'unknown',
  run: 'runTasks',
  finish: 'finishTasks',
  close: 'closeTasks',
  'unmatched-task': 'unmatchedTask',
};

const labels = {
  'all-orders': '全部订单',
  'dispatch-in-progress': '派工在制订单',
  'dispatch-completed': '已完成订单',
  'dispatch-closed': '已关闭订单',
  'task-not-generated': '未形成当前任务订单',
  unknown: '状态待确认订单',
  run: '运行中任务',
  finish: '已完成任务',
  close: '已关闭任务',
  'unmatched-task': '任务来源未取得',
};

export function selectOrderDispatchRows(spec, filter = 'all-orders', line = '') {
  const groups = spec?.detailGroups || {};
  const rows = groups[groupKeys[filter] || 'allOrders'] || groups.allOrders || [];
  return line ? rows.filter(row => (row.line || '暂无法确认') === line) : rows;
}

export function orderDispatchFilterLabel(filter = 'all-orders') {
  return labels[filter] || labels['all-orders'];
}

export function buildTaskStatusByLine(spec) {
  const tasks = spec?.detailGroups?.allTasks || spec?.taskRows || [];
  const grouped = new Map();
  for (const row of tasks) {
    const line = row.line || '暂无法确认';
    const item = grouped.get(line) || { line, run: 0, finish: 0, close: 0, total: 0 };
    if (row.statusKey === 'run') item.run += 1;
    if (row.statusKey === 'finish') item.finish += 1;
    if (row.statusKey === 'close') item.close += 1;
    item.total += 1;
    grouped.set(line, item);
  }
  return [...grouped.values()].sort((a, b) => b.total - a.total || a.line.localeCompare(b.line, 'zh-CN'));
}
