import {samePlanBase} from './demo-plan.js';
export const PREVIEW_SIZE = 5;
export const DETAIL_PAGE_SIZE = 20;
export const number = value => Number(value || 0).toLocaleString('zh-CN', {maximumFractionDigits: 4});
export const unitName = unit => unit === 'KG' ? '公斤' : unit || '';
export const statusName = status => ({run:'运行中',finish:'已完成',close:'已关闭'}[status] || '状态待核对');
export const inProgressName = tag => tag === 'T' ? '派工在制' : tag === 'F' ? '无在制标记' : '状态待核对';
export function selectPage(rows = [], {query = '', page = 1, pageSize = DETAIL_PAGE_SIZE, filters = {}, keys} = {}) {
 const text = String(query).trim().toLocaleLowerCase();
 const filtered = rows.filter(row => Object.entries(filters).every(([key,value]) => value === '' || value == null || String(row[key]) === String(value)) && (!text || (keys || Object.keys(row)).some(key => String(row[key] ?? '').toLocaleLowerCase().includes(text))));
 const size = Math.max(1, Math.trunc(pageSize) || DETAIL_PAGE_SIZE), pages = Math.max(1, Math.ceil(filtered.length / size));
 const current = Math.min(pages, Math.max(1, Math.trunc(page) || 1)), offset = (current - 1) * size;
 return {rows: filtered.slice(offset, offset + size), total: rows.length, filteredTotal: filtered.length, page: current, pages, start: filtered.length ? offset + 1 : 0, end: Math.min(offset + size, filtered.length)};
}
export function lateDays(order) {
 const end = Date.parse(order.planEnd), due = Date.parse(order.dueDate);
 return Number.isFinite(end) && Number.isFinite(due) ? Math.max(0, Math.round((end - due) / 86400000)) : 0;
}
export function sortedOrders(spec) {
 return [...(spec.orders || [])].sort((a,b) => (b.orderId === spec.orderId) - (a.orderId === spec.orderId) || lateDays(b) - lateDays(a) || (a.position || 0) - (b.position || 0) || a.orderId.localeCompare(b.orderId));
}
export const orderPreview = spec => sortedOrders(spec).slice(0, PREVIEW_SIZE);
export function materialFocusRows(rows = []) {
 const terminal = rows.filter(row => !row.active);
 return [...(terminal.length ? terminal : rows)].sort((a,b) => Number(b.remaining > 0) - Number(a.remaining > 0) || String(b.level || '').split('.').length - String(a.level || '').split('.').length || String(a.orderId).localeCompare(String(b.orderId)) || String(a.material).localeCompare(String(b.material)));
}
export function operationState(spec = {}, current = {}, {cancelled = false, loading = false, completed = false} = {}) {
 current ||= {};
 if (spec.archived) return {key:'archived',label:'历史记录',canExecute:false};
 if (loading) return {key:'running',label:spec.view === 'mrp' ? '正在核算物料需求…' : spec.view === 'release' ? '正在下发计划…' : '正在试排并比较影响…',canExecute:false};
 if (spec.receipt) return !spec.native || current.nativeReleaseId === spec.proposedPlan?.nativeReleaseId && current.releaseStatus === 'released' ? {key:'released',label:'已下发',canExecute:false} : {key:'superseded',label:'这是之前的下发记录，请查询当前计划。',canExecute:false};
 if (spec.proposedPlan && ['reschedule','insert'].includes(spec.view) && current.nativeResultId === spec.calculationId && current.revision > spec.baseRevision) return {key:'adopted',label:'已采用此方案',canExecute:false};
 if (spec.proposedPlan && spec.view === 'mrp' && current.nativeMrpResultId === spec.mrpResultId && current.revision > spec.baseRevision) return {key:'saved',label:'已保存核算结果',canExecute:false};
 if (current.releaseStatus === 'released' && (spec.prepare || spec.proposedPlan)) return {key:'locked',label:'当前计划已下发，无需重复操作。',canExecute:false};
 if ((spec.prepare || spec.proposedPlan) && (current.revision !== spec.baseRevision || (spec.native && !samePlanBase(current,spec.baseState)) || (spec.baseState?.nativeResultId && current.nativeResultId !== spec.baseState.nativeResultId))) return {key:'superseded',label:'计划或物料信息已更新，请按当前方案重新准备。',canExecute:false};
 // Persisted UI flags can lag behind an already committed workspace transaction.
 if (cancelled) return {key:'cancelled',label:'已取消此次操作。',canExecute:false};
 if (completed) return {key:'completed',label:'已完成，请查看新的结果。',canExecute:false};
 if (spec.prepare) return {key:'confirmation',label:'待确认',canExecute:true};
 if (spec.proposedPlan) return {key:'ready',label:spec.view === 'mrp' ? '核算完成，待保存' : '试排完成，待采用',canExecute:true};
 if (spec.native && spec.baseState && !samePlanBase(current,spec.baseState)) return {key:'snapshot',label:'保留查询时的结果；当前计划或物料信息已有更新。',canExecute:false};
 return {key:'view',label:'',canExecute:false};
}
export function relatedActions(workspace = {}) {
 const id = workspace.focusOrderId || (workspace.orders?.length === 1 ? workspace.orders[0].orderId : '');
 const subject = id ? '订单 ' + id : '这批订单';
 const schedule = {label:'查看排程',question:'查看' + subject + '的排程'}, materials = {label:'核对物料',question:'查看' + subject + '的物料和已保存MRP依据'}, dispatch = {label:'查看派工',question:'查看' + subject + '的派工情况'}, risks = {label:'查看交期风险',question:'查看这批订单的交期风险'};
 if (['mrp','material'].includes(workspace.view)) return [schedule,dispatch];
 if (workspace.view === 'dispatch') return [schedule,materials];
 if (workspace.view === 'order') return [materials,dispatch];
 if (['reschedule','insert'].includes(workspace.view)) return id ? [schedule,risks] : [risks,materials];
 if (workspace.view === 'release') return [schedule,dispatch];
 return [risks,materials];
}
export function businessTitle(spec = {}) {
 if (spec.archived) return '历史计划记录';
 if (spec.prepare) return ({insert:'生产计划调整确认',reschedule:'生产计划调整确认',mrp:'物料需求计算确认',release:'计划下发确认'})[spec.view] || '生产计划调整确认';
 return ({insert:'生产计划总览',reschedule:'生产计划总览',overview:'生产计划总览',risk:'交期风险',order:'订单排程',material:'物料需求核对',mrp:'物料需求计算结果',dispatch:'派工情况',release:'计划下发完成'})[spec.view] || '生产计划总览';
}
export function businessMetrics(spec = {}) {
 const orders = spec.orders || [], tasks = spec.tasks || [], focus = materialFocusRows(spec.mrpRows || []);
 if (spec.view === 'dispatch') {
  const scope=new Set(orders.map(o=>o.orderId)),linked=new Set(tasks.flatMap(t=>t.orderIds||[]).filter(id=>scope.has(id)));
  return [{label:'关联任务',value:tasks.length},{label:'运行中',value:tasks.filter(t=>t.state_type === 'run').length},{label:'关联订单',value:linked.size}];
 }
 if (['material','mrp'].includes(spec.view)) return [{label:'核对订单',value:orders.length},{label:'物料核算行',value:(spec.mrpRows || []).length},{label:'需补充物料项',value:focus.filter(r=>r.remaining > 0).length}];
 if (spec.view === 'order' && orders.length === 1) return [{label:'计划开始',value:orders[0].planStart || '待核对'},{label:'计划结束',value:orders[0].planEnd || '待核对'},{label:'订单交期',value:orders[0].dueDate || '待核对'}];
 if (spec.view === 'release') return [{label:'下发订单',value:orders.length},{label:'当前状态',value:'已下发'},{label:'关联任务',value:tasks.length}];
 if (spec.view === 'risk') return [{label:'风险订单',value:orders.length},{label:'最大计划超期',value:orders.length ? Math.max(...orders.map(lateDays)) + '天' : '0天'}];
 return [{label:'订单数量',value:orders.length},{label:'交期风险',value:orders.filter(o=>o.risk).length},{label:'派工在制',value:orders.filter(o=>o.taskTag === 'T').length}];
}
export function businessHeadline(spec = {}) {
 const orders = spec.orders || [], id = spec.orderId || '';
 if (spec.prepare && spec.view === 'insert') return (id ? '将订单 '+id : '请选择要加入的订单')+'加入当前生产计划，核对后重新排程。确认采用前保留原计划。';
 if (spec.prepare) return spec.view === 'release' ? '请核对订单范围和计划日期，确认后继续下发。' : spec.view === 'mrp' ? '确认后核算本批物料需求，生产计划保持不变。' : '请核对目标订单和顺序，确认后重新试排。';
 if (spec.view === 'dispatch') return '查到' + (spec.tasks || []).length + '条关联任务，可继续查看各工序状态。';
 if (['material','mrp'].includes(spec.view)) {const row=materialFocusRows(spec.mrpRows)[0];return row ? row.orderId + '的物料' + row.material + '，扣除库存和在途后，剩余需求为' + number(row.remaining) + unitName(row.unit) + '。' : '当前范围未查到可核对的物料结果。';}
 if (['reschedule','insert'].includes(spec.view)) return '已完成' + (spec.changes || []).length + '张订单的试排，可比较计划变化后采用。';
 if (spec.view === 'release') return '这批' + orders.length + '张订单的计划已下发，已有任务保留。';
 if (spec.view === 'order' && orders.length === 1) return (id || orders[0].orderId) + '计划安排在' + (orders[0].planStart || '待核对') + '至' + (orders[0].planEnd || '待核对') + '。';
 return '本批' + orders.length + '张订单中，' + orders.filter(o=>o.planEnd).length + '张已有计划，' + orders.filter(o=>o.risk).length + '张存在交期风险。';
}
export function visibleAssistantText(message = {}) {
 const text = message.content || '';
 if (message.role !== 'assistant') return text;
 if (message.uiBlocks?.some(block=>block.type==='interactive-demo'&&!block.spec?.native)) return '这条记录保留了当时的计划信息，可以查看明细核对。当前操作请在新对话中进行。';
 const legacy = message.uiBlocks?.find(block=>block.spec?.native && block.spec.presentationVersion !== 2);
 if (legacy) return businessHeadline(legacy.spec);
 if (/^已采用需求版本/.test(text)) return '已采用这次试排方案，后续查询将使用当前计划。';
 if (/^已保存独立MRP结果/.test(text)) return '本次物料核算已保存，生产计划保持不变。';
 if (/^已恢复需求版本/.test(text)) return '已恢复原方案，物料核算结果保留。';
 return text;
}
export function historicalBlock(block){
 const original=block.spec,orders=original.orders||original.after?.orders||original.before?.orders||[];
 const materials=orders.filter(o=>o.material).map(o=>({detailGuid:'history-'+o.orderId,orderId:o.orderId,material:o.material.materialNo||'钢材',active:'',level:'原记录',beforeOffset:o.material.grossNeed,stockUsed:o.material.stockUsed,transitUsed:o.material.transitUsed,remaining:o.material.purchaseNeed,unit:'KG'}));
 const chart=original.afterChart||original.chart;
 return {...block,title:'历史计划记录',spec:{...original,archived:true,prepare:false,proposedPlan:undefined,workspace:undefined,view:original.view==='setup'?'overview':original.view,orders,allOrders:orders,mrpRows:original.mrpRows||materials,materialRows:[],tasks:[],chart:chart?{...chart,fixedColumns:chart.fixedColumns||[{key:'lineName',label:'产线'},{key:'productName',label:'产品'},{key:'stage',label:'工序'},{key:'orderId',label:'订单号'}]}:undefined}};
}
export function currentBusinessBlock(block,currentVersion){
 const spec=block.spec;
 if(spec.archived)return block;
 if(!spec.native)return historicalBlock(block);
 if(spec.demandVersion===currentVersion)return block;
 return {...block,title:'历史计划记录',spec:{...spec,archived:true,prepare:false,proposedPlan:undefined,workspace:undefined}};
}
export function scopedChart(chart = {}, selectedIds = []) {
 const selected = new Set(selectedIds), indexed = (chart.rows || []).map((row,index)=>({row,index})).filter(({row})=>selected.has(row.orderId));
 const indexMap = new Map(indexed.map(({index},i)=>[index,i]));
 return {...chart,rows:indexed.map(x=>x.row),items:(chart.items || []).filter(item=>indexMap.has(item.rowIndex)).map(item=>({...item,rowIndex:indexMap.get(item.rowIndex)}))};
}
