export const hoursText = value => Number.isFinite(value) ? `${Number(value.toFixed(2))} h` : '未提供';
export const loadText = load => load?.status === 'zero-capacity' ? '零可用工时仍有排入'
  : Number.isFinite(load?.ratio) ? `${(load.ratio * 100).toFixed(1)}%` : '工时数据不足';

export function taskDetails(snapshot, task) {
  const order = snapshot.orders.find(o => o.id === task.orderId);
  const line = snapshot.lines.find(l => l.id === task.lineId);
  return [
    ['销售合同 / 订单', task.orderId],
    ['订单品名', order?.product || '未提供'],
    ['订单规格', order?.spec || '未提供'],
    ['部件品名', task.componentName || '未提供'],
    ['物料名称', task.materialName || '未提供'],
    ['物料编码', task.materialCode || '未提供'],
    ['物料规格', task.materialSpec || '未提供'],
    ['工段 / 工序', `${task.stage} / ${task.operation}`],
    ['产线', line?.name || task.lineId],
    ['日期 / 班次', `${task.date} ${task.shift}`],
    ['计划数量', `${task.quantity.toLocaleString()} 件`],
    ['计划占用工时', hoursText(task.plannedHours)],
  ];
}

// Both scenes use the same task IDs. Never derive hours from counts or quantities.
export function aggregateLineDays(tasks) {
  const days = new Map();
  for (const task of tasks) {
    const key = `${task.lineId}/${task.date}`;
    if (!days.has(key)) days.set(key, {tasks:[],knownHours:0,missingHours:0});
    const day = days.get(key);
    day.tasks.push(task);
    if (Number.isFinite(task.plannedHours) && task.plannedHours >= 0) day.knownHours += task.plannedHours;
    else day.missingHours++;
  }
  return days;
}

export function calculateLoad(day, capacity, covered) {
  const hours = day?.knownHours || 0, missingHours = day?.missingHours || 0;
  if (!covered || missingHours || !Number.isFinite(capacity) || capacity < 0)
    return {plannedHours:covered && !missingHours ? hours : null,capacityHours:capacity ?? null,ratio:null,status:'unknown'};
  if (capacity === 0) return {plannedHours:hours,capacityHours:0,ratio:hours ? null : 0,status:hours ? 'zero-capacity' : 'off'};
  return {plannedHours:hours,capacityHours:capacity,ratio:hours/capacity,status:hours>capacity?'overload':'known'};
}

export function loadColor(load) {
  if (load.status === 'unknown') return '#e8edf3';
  if (load.status === 'off') return '#d4d8df';
  if (load.status === 'zero-capacity' || load.ratio > 1) return '#ce665a';
  return load.ratio > .8 ? '#d9a14b' : load.ratio > .5 ? '#3aaba5' : load.ratio > 0 ? '#82c7be' : '#d8eceb';
}
