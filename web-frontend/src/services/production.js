import {aggregateLineDays,calculateLoad,loadColor,loadText,hoursText,taskDetails} from './production-details.js';
export const DAY = 86400000;
export const dayNumber = (date) => Date.parse(`${date}T00:00:00Z`) / DAY;
export const addDays = (date, n) => new Date((dayNumber(date) + n) * DAY).toISOString().slice(0, 10);
export const statusText = { full: '已排完整', partial: '部分已排', none: '尚未排入' };
export const productionText = { completed: '已完工', inProgress: '生产中', notStarted: '未开工' };
export function selectProduction(snapshot, filter = {}) {
  const lineIds = new Set(snapshot.lines.filter(l => (!filter.lineId || l.id === filter.lineId) && (!filter.stage || l.stage === filter.stage)).map(l => l.id));
  const matchingTasks = snapshot.tasks.filter(t => lineIds.has(t.lineId) && (!filter.date || t.date === filter.date));
  const orderIds = new Set(matchingTasks.map(t => t.orderId));
  const pendingIds = new Set(snapshot.pending.map(t => t.orderId));
  const orders = snapshot.orders.filter(o => o.productionStatus !== 'completed' &&
    (!filter.orderId || o.id === filter.orderId) &&
    (filter.scope !== 'risk' || o.risk) && (filter.scope !== 'pending' || pendingIds.has(o.id)) &&
    (!(filter.lineId || filter.stage || filter.date) || orderIds.has(o.id)));
  const ids = new Set(orders.map(o => o.id));
  return { orders, lines: snapshot.lines.filter(l => lineIds.has(l.id)),
    tasks: matchingTasks.filter(t => ids.has(t.orderId)),
    pending: snapshot.pending.filter(t => ids.has(t.orderId) && (!filter.stage || t.stage === filter.stage)) };
}
export function summarize(selection) {
  return { total: selection.orders.length, full: selection.orders.filter(o => o.scheduleStatus === 'full').length,
    partial: selection.orders.filter(o => o.scheduleStatus === 'partial').length,
    none: selection.orders.filter(o => o.scheduleStatus === 'none').length,
    risk: selection.orders.filter(o => o.risk).length, pending: selection.pending.length };
}
export function calendarDays(year) {
  const first = `${year}-01-01`, start = addDays(first, -((new Date(first+'T00:00:00Z').getUTCDay()+6)%7));
  const days = [];
  for (let date = first; date <= `${year}-12-31`; date = addDays(date, 1)) {
    const n = dayNumber(date)-dayNumber(start);
    days.push({ date, week: Math.floor(n/7), weekday: n%7 });
  }
  return days;
}
export function calendarState(snapshot, date, count) {
  if (date < snapshot.coverageStart || date > snapshot.coverageEnd) return 'unknown';
  if (count) return 'planned';
  if (snapshot.nonWorkingWeekdays.includes(new Date(date+'T00:00:00Z').getUTCDay())) return 'off';
  return 'empty';
}
export const calendarColors = { unknown: '#e8edf3', off: '#d4d8df', empty: '#d8eceb' };
export const calendarUnknownHoursColors = { planned: '#7496cc', empty: '#dbe5f2' };
export function makeProductionScene(snapshot, mode, filter = {}, options = {}) {
  const selection = selectProduction(snapshot, filter), cells = [], labels = [], guides = [];
  const spacing = Number(options.spacing || 2), dateSpacing=Number(options.dateSpacing||1.6), lineSpacing=Number(options.lineSpacing||1.8);
  const layout={spacing,dateSpacing,lineSpacing};
  const contentKey=JSON.stringify({mode,filter,start:options.start,end:options.end,snapshot:snapshot.id});
  if (mode === 'calendar') {
    const days = calendarDays(snapshot.year), grouped=aggregateLineDays(selection.tasks), allDays=aggregateLineDays(selectProduction(snapshot).tasks);
    const capacity=new Map((snapshot.capacity||[]).map(c=>[`${c.lineId}/${c.date}`,c.availableHours]));
    selection.lines.forEach((line, i) => {
      const y = 0.4 + i * 2.3 * spacing;
      labels.push({ text: line.name, axis:'y', x: -2, y, z: 6.5*lineSpacing, color: '#42616b' });
      for (const day of days) {
        const key=`${line.id}/${day.date}`, entry=grouped.get(key), count=entry?.tasks.length||0, state=calendarState(snapshot,day.date,count);
        const covered=day.date>=snapshot.coverageStart&&day.date<=snapshot.coverageEnd;
        const load=calculateLoad(entry,capacity.get(key),covered),lineLoad=calculateLoad(allDays.get(key),capacity.get(key),covered);
        const tooltip=[['产线 / 日期',`${line.name} · ${day.date}`],['当前筛选任务',`${count} 项班次任务`],['所选计划工时',hoursText(load.plannedHours)],['可用工时上限',hoursText(load.capacityHours)],['所选计划负载',loadText(load)],['全线未完订单负载',loadText(lineLoad)],['工时来源',snapshot.hoursSource==='synthetic-demo'?'合成演示值 · 非实时':'未提供']];
        cells.push({ id:key, x:day.week*.64*dateSpacing, y, z:day.weekday*.85*lineSpacing,
          w:.56, h:.11, d:.74, color:load.status==='unknown'
            ? count>0?calendarUnknownHoursColors.planned:state==='empty'?calendarUnknownHoursColors.empty:calendarColors[state]
            : loadColor(load),
          kind:'day', lineId:line.id, date:day.date, count, state,load,lineLoad,taskIds:(entry?.tasks||[]).map(t=>t.id),tooltip,
          label:`${line.name} · ${day.date} · ${count} 项班次任务 · ${loadText(load)}` });
      }
    });
    for (let m=1;m<=12;m++) {
      const day=days.find(d=>d.date===`${snapshot.year}-${String(m).padStart(2,'0')}-01`);
      labels.push({text:`${m}月`,axis:'x',x:day.week*.64*dateSpacing,y:0,z:-1,color:'#657587'});
    }
    ['一','二','三','四','五','六','日'].forEach((text,i)=>labels.push({text:`周${text}`,axis:'z',x:-1.5,y:0,z:i*.85*lineSpacing,color:'#82909f'}));
    return { cells, labels, guides, width:(days.at(-1).week+1)*.64*dateSpacing, depth:7*lineSpacing, height:Math.max(1,selection.lines.length*2.3*spacing), selection,layout,contentKey };
  }
  const start=options.start || snapshot.defaultStart, end=options.end || snapshot.defaultEnd;
  const days=Math.max(1, Math.min(366, dayNumber(end)-dayNumber(start)+1));
  const orderIndex=new Map(selection.orders.map((o,i)=>[o.id,i])), lineIndex=new Map(selection.lines.map((l,i)=>[l.id,i]));
  for(const task of selection.tasks) {
    const n=dayNumber(task.date)-dayNumber(start);
    if(n<0||n>=days) continue;
    const order=selection.orders[orderIndex.get(task.orderId)], y=.65+orderIndex.get(task.orderId)*1.05*spacing;
    cells.push({id:task.id,x:(n*.85+(task.shift==='晚班'?.22:-.22))*dateSpacing,y,z:lineIndex.get(task.lineId)*2*lineSpacing,
      w:.36,h:.28,d:1.35,color:order.color,kind:'task',task,tooltip:[...taskDetails(snapshot,task),['数据来源','本地合成示例 · 工时为演示值']],
      label:`${task.orderId} · ${task.date} ${task.shift} · ${order.product} · ${task.quantity.toLocaleString()} 件`});
  }
  selection.orders.forEach((order,i)=> {
    const y=.65+i*1.05*spacing;
    labels.push({text:order.id,axis:'y',x:-2,y,z:Math.max(2,selection.lines.length*2*lineSpacing),color:order.color});
    const due=dayNumber(order.dueDate)-dayNumber(start);
    if(filter.orderId && due>=0 && due<days) {
      guides.push({from:[due*.85*dateSpacing,y,-1],to:[due*.85*dateSpacing,y,Math.max(1,selection.lines.length*2*lineSpacing-1)],color:'#e29956'});
      labels.push({text:`交期 ${order.dueDate.slice(5)}`,axis:'x',x:due*.85*dateSpacing,y:y+.7,z:-1.4,color:'#b77738'});
    }
  });
  selection.lines.forEach((line,i)=>labels.push({text:line.name,axis:'z',x:-2,y:0,z:i*2*lineSpacing,color:'#6c7f93'}));
  for(let i=0;i<days;i++) {
    guides.push({from:[i*.85*dateSpacing,0,-1],to:[i*.85*dateSpacing,0,Math.max(1,selection.lines.length*2*lineSpacing-1)],color:'#e0e8f1'});
    labels.push({text:addDays(start,i).slice(5),axis:'x',x:i*.85*dateSpacing,y:0,z:-2,color:'#6c7f93'});
  }
  for(let i=0;i<selection.lines.length;i++) guides.push({from:[-.4,0,i*2*lineSpacing],to:[days*.85*dateSpacing,0,i*2*lineSpacing],color:'#d8e3ef'});
  const today=dayNumber(snapshot.asOf)-dayNumber(start);
  if(today>=0&&today<days) guides.push({from:[today*.85*dateSpacing,0,-1],to:[today*.85*dateSpacing,0,Math.max(1,selection.lines.length*2*lineSpacing-1)],color:'#599de5'});
  return {cells,labels,guides,width:days*.85*dateSpacing,depth:Math.max(4,selection.lines.length*2*lineSpacing+1),height:Math.max(2,selection.orders.length*1.05*spacing),selection,layout,contentKey};
}
