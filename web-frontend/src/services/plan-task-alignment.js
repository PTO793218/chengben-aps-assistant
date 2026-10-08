const groupKeys = {
  all: 'all',
  linked: 'linked',
  'unlinked-plan': 'unlinkedPlan',
  run: 'run',
  finish: 'finish',
  close: 'close',
  orphan: 'orphan',
  'orphan-run': 'orphanRun',
};

const labels = {
  all: '全部计划与任务记录',
  linked: '已找到当前任务的生产计划',
  'unlinked-plan': '暂未找到当前任务的生产计划',
  run: '运行中任务',
  finish: '已完成任务',
  close: '已关闭任务',
  orphan: '暂未找到来源生产计划的任务',
  'orphan-run': '运行中且暂未找到来源计划的任务',
};

export function selectAlignmentRows(spec, filter = 'all') {
  const groups = spec?.detailGroups || {};
  return groups[groupKeys[filter] || 'all'] || groups.all || [];
}

export function alignmentFilterLabel(filter = 'all') {
  return labels[filter] || labels.all;
}

export function buildTaskStatusByLine(spec) {
  const tasks = (spec?.detailGroups?.all || []).filter(row => row.rowType === 'task');
  const grouped = new Map();
  for (const row of tasks) {
    const line = row.line || '暂无法确认';
    const item = grouped.get(line) || { line, run: 0, finish: 0, close: 0, total: 0 };
    if (row.status === '运行中') item.run += 1;
    if (row.status === '已完成') item.finish += 1;
    if (row.status === '已关闭') item.close += 1;
    item.total += 1;
    grouped.set(line, item);
  }
  return [...grouped.values()].sort((a, b) => b.total - a.total || a.line.localeCompare(b.line, 'zh-CN'));
}
