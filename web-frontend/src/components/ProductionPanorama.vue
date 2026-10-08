<template>
  <section class="production-panorama" aria-label="生产计划全景">
    <header class="production-panorama-header">
      <div class="production-header-main"><button class="icon-button" aria-label="关闭全景" @click="$emit('close')"><Icon name="close"/></button><div><span class="production-eyebrow">CHENGBEN · PLANNING SPACE</span><h2>{{ mode==='schedule'?'订单排程全景':'产线日历全景' }}</h2></div><span class="production-demo">本地示例</span></div>
      <div class="production-tabs"><button :class="{active:mode==='schedule'}" @click="switchMode('schedule')">订单排程</button><button :class="{active:mode==='calendar'}" @click="switchMode('calendar')">年度日历</button></div>
    </header>
    <div class="production-snapshot">{{ snapshot.id }} · 数据时点 {{ snapshot.asOf }} · 全部未完工订单基集 · {{ mode==='schedule'?'高度＝订单层，非产量':'高度＝产线层，颜色＝所选计划工时 / 可用工时上限' }}</div>
    <div class="production-filters">
      <label>订单范围<select v-model="filter.scope" aria-label="订单范围"><option value="all">全部未完工</option><option value="risk">计划交期风险</option></select></label>
      <label>工段<select v-model="filter.stage" aria-label="工段筛选" @change="filter.lineId='' ; selected=null"><option value="">全部工段</option><option v-for="stage in stages" :key="stage">{{ stage }}</option></select></label>
      <label>产线<select v-model="filter.lineId" aria-label="产线筛选" @change="selected=null"><option value="">全部产线</option><option v-for="line in availableLines" :value="line.id" :key="line.id">{{ line.name }}</option></select></label>
      <label>订单<select v-model="filter.orderId" aria-label="订单筛选" @change="selected=null"><option value="">全部订单</option><option v-for="order in unfinished" :value="order.id" :key="order.id">{{ order.id }}</option></select></label>
      <template v-if="mode==='schedule'"><label>开始<input v-model="start" type="date" aria-label="开始日期" :min="snapshot.year+'-01-01'" :max="snapshot.year+'-12-31'"></label><label>结束<input v-model="end" type="date" aria-label="结束日期" :min="start" :max="snapshot.year+'-12-31'"></label></template>
      <template v-else><span class="production-year">{{ snapshot.year }} 年 · 每列七天</span><label>定位日期<input type="date" aria-label="日历定位日期" :min="snapshot.year+'-01-01'" :max="snapshot.year+'-12-31'" @change="inspectDate($event.target.value)"></label></template>
      <button class="production-text-button" @click="clearFilters">清除筛选</button>
    </div>
    <div class="production-workspace">
      <div class="production-canvas-area">
        <div class="production-scene-toolbar"><div><span class="production-count">{{ selection.orders.length }} 个订单</span><span>{{ mode==='schedule'?visibleTasks.length+' 项班次任务':selection.lines.length+' 层产线日历 · '+selection.tasks.length+' 项班次任务' }}</span></div><div><label>层距<input v-model="spacing" aria-label="层间距离" type="range" min="1" max="4" step="0.2"><output>{{ spacing }}×</output></label><label>{{ mode==='schedule'?'日期':'周列' }}间距<input v-model="dateSpacing" aria-label="日期间距" type="range" min="1" max="3" step="0.2"><output>{{ dateSpacing }}×</output></label><label>{{ mode==='schedule'?'产线':'星期' }}间距<input v-model="lineSpacing" aria-label="产线间距" type="range" min="1" max="4" step="0.2"><output>{{ lineSpacing }}×</output></label><label><input v-model="autoSnap" type="checkbox" aria-label="自动吸附平面">自动吸附</label><button v-for="view in views" :key="view.id" :aria-pressed="activeView===view.id" :title="view.title" @click="scene?.setView(view.id)">{{ view.name }}</button><button @click="scene?.reset()">复位视角</button></div></div>
        <div class="production-view-caption">{{ viewCaption }}<span v-if="mode==='schedule'">一条＝一个班次任务；同一天白班在左、晚班在右。拉大间距后可平移或缩小查看全图。</span></div>
        <div v-if="mode==='calendar' && missingHoursDays" class="production-calendar-notice" role="status">当前快照有 {{ selection.tasks.length }} 项已排班次；其中 {{ missingHoursDays }} 个产线日期缺少工时，已用蓝色标记排程。点击格子仍可查看班次明细。</div>
        <div v-if="invalidRange" class="production-empty">请选择有效的开始和结束日期。</div>
        <ProductionScene v-else ref="scene" :model="model" :auto-snap="autoSnap" :selected-id="selected?.id || selected?.task?.id" @select="selectCell" @view-change="activeView=$event"/>
        <div v-if="!invalidRange && mode==='schedule' && !visibleTasks.length" class="production-empty-overlay">当前日期范围没有已排任务<span>可查看待排区，或展开完整计划区间。</span><button @click="showAllDates">完整计划区间</button></div>
        <div class="production-scene-legend" v-if="mode==='schedule'"><span><i style="background:#599de5"></i>蓝色地面线：快照日期</span><span>任务颜色区分订单 · 点击任务查看详情</span></div>
        <div class="production-scene-legend" v-else><span v-for="legend in calendarLegend" :key="legend.text"><i :style="{background:legend.color}"></i>{{ legend.text }}</span></div>
        <footer class="production-canvas-footer"><span>拖动旋转 · 滚轮缩放 · 右键平移 · 点击选中</span><button v-if="mode==='schedule'" @click="showAllDates">完整计划区间（{{ outsideCount }} 项在当前区间外）</button><span v-else>样本覆盖 {{ snapshot.coverageStart }} — {{ snapshot.coverageEnd }}</span></footer>
      </div>
      <aside class="production-inspector" aria-label="计划明细">
        <template v-if="selected?.kind==='task'">
          <div class="production-inspector-title"><span class="production-eyebrow">SELECTED TASK</span><button class="icon-button" aria-label="取消选中任务" @click="selected=null">×</button></div>
          <h3>{{ selected.task.orderId }}</h3><p>{{ selectedOrder?.product }}</p>
          <dl class="production-detail-grid"><template v-for="[name,value] in selectedMaterialDetails" :key="name"><dt>{{ name }}</dt><dd>{{ value }}</dd></template><dt>工段 / 工序</dt><dd>{{ selected.task.stage }} / {{ selected.task.operation }}</dd><dt>产线</dt><dd>{{ lineName(selected.task.lineId) }}</dd><dt>日期 / 班次</dt><dd>{{ selected.task.date }} {{ selected.task.shift }}</dd><dt>计划数量</dt><dd>{{ selected.task.quantity.toLocaleString() }} 件</dd><dt>订单需求</dt><dd>{{ selectedOrder?.quantity.toLocaleString() }} 件</dd><dt>订单交期</dt><dd>{{ selectedOrder?.dueDate }}</dd><dt>计划结束</dt><dd>{{ selectedOrder?.plannedEnd || '未排完整，尚不确定' }}</dd><dt>生产状态</dt><dd>{{ productionText[selectedOrder?.productionStatus] }}</dd></dl>
          <button class="production-primary" @click="filter.orderId=selected.task.orderId;filter.lineId='';filter.stage='';showAllDates()">聚焦该订单的跨线安排</button>
          <button class="production-secondary" @click="askOrder(selected.task.orderId)">回到聊天，继续问这个订单</button>
        </template>
        <template v-else-if="selected?.kind==='day'">
          <span class="production-eyebrow">LINE / DAY</span><h3>{{ selected.date }}</h3><p>{{ lineName(selected.lineId) }}</p>
          <div class="production-day-count">{{ loadText(selected.load) }}<small>所选计划负载</small></div><dl class="production-detail-grid"><dt>计划 / 上限</dt><dd>{{ hoursText(selected.load?.plannedHours) }} / {{ hoursText(selected.load?.capacityHours) }}</dd><dt>全线负载</dt><dd>{{ loadText(selected.lineLoad) }}</dd><dt>所选班次</dt><dd>{{ selected.count }} 项</dd></dl><p class="production-note">计划工时 ÷ 可用工时上限。全线负载按全部未完工订单计算；工时为合成演示值。</p>
          <p v-if="selected.state==='unknown'" class="production-note">该日期没有样本数据，不能判断为空闲。</p>
          <p v-else-if="selected.state==='off'" class="production-note">示例工作日历标记为非工作日。</p>
          <p v-else-if="!selected.count" class="production-note">当前筛选没有计划任务，不代表产线可用能力。</p>
          <button class="production-primary" @click="filter.lineId=selected.lineId;selected=null">只展开这条产线</button>
          <button class="production-secondary" @click="jumpToDay">查看这一天的订单排程</button>
        </template>
        <template v-else>
          <span class="production-eyebrow">PLAN AT A GLANCE</span><h3>{{ filter.orderId || '计划概况' }}</h3><p>从一个订单，追踪它在不同产线上的安排。</p>
          <div class="production-side-metrics"><div><b>{{ counts.full }}</b><span>已排完整</span></div><div><b>{{ counts.partial }}</b><span>部分已排</span></div><div><b>{{ counts.none }}</b><span>尚未排入</span></div></div>
          <div class="production-risk-note">{{ counts.risk }} 个计划交期风险</div>
          <p class="production-note">已排完整 ≠ 已生产完成。当前只读展示合成样本，不执行改排。</p>
          <button v-if="filter.orderId" class="production-secondary" @click="askOrder(filter.orderId)">回到聊天，继续问这个订单</button>
        </template>
        <div class="production-inspector-tabs"><button :class="{active:panel==='orders'}" @click="panel='orders'">订单 {{ selection.orders.length }}</button><button :class="{active:panel==='tasks'}" @click="panel='tasks'">班次明细</button></div>
        <div v-if="panel==='orders'" class="production-order-list">
          <button v-for="order in selection.orders" :key="order.id" class="production-order-item" @click="filter.orderId=order.id; selected=null">
            <div><i :style="{background:order.color}"></i><strong>{{ order.id }}</strong><span v-if="order.risk" class="production-risk-tag">交期风险</span></div><small>{{ order.product }}</small><div><span>{{ statusText[order.scheduleStatus] }}</span><span>交期 {{ order.dueDate.slice(5) }}</span></div>
          </button><p v-if="!selection.orders.length" class="production-note">当前筛选无匹配订单，可清除筛选。</p>
        </div>
        <div v-if="panel==='tasks'" class="production-task-list"><button v-for="task in detailTasks" :key="task.id" @click="selected={kind:'task',task}"><strong>{{ task.orderId }} · {{ task.stage }}</strong><span>{{ task.date }} {{ task.shift }} · {{ task.quantity.toLocaleString() }} 件</span><small>{{ lineName(task.lineId) }}</small></button><p v-if="!detailTasks.length" class="production-note">当前范围无班次任务。</p></div>
      </aside>
    </div>
  </section>
</template>
<script setup>
import {computed,ref,defineAsyncComponent,watch} from 'vue';
import Icon from './Icon.vue';
import {hoursText,loadText,taskDetails} from '../services/production-details.js';
import {makeProductionScene,selectProduction,summarize,statusText,productionText,dayNumber} from '../services/production';
const ProductionScene=defineAsyncComponent(()=>import('./ProductionScene.vue'));
const props=defineProps({item:Object});const emit=defineEmits(['close','ask']);
const snapshot=props.item.spec.snapshot, initial=props.item.spec.focus||{};
const mode=ref(props.item.mode||'schedule'),filter=ref({scope:'all',stage:'',orderId:'',lineId:'',...initial,date:''});
const start=ref(initial.date||snapshot.defaultStart),end=ref(initial.date||snapshot.defaultEnd),spacing=ref(2),selected=ref(null),panel=ref('orders'),scene=ref();
const dateSpacing=ref(1.6),lineSpacing=ref(1.8),autoSnap=ref(true),activeView=ref('free');
const views=computed(()=>[{id:'top',name:'俯视',title:mode.value==='schedule'?'日期 × 产线':'周次 × 星期'},{id:'front',name:'正视',title:mode.value==='schedule'?'日期 × 订单':'周次 × 产线'},{id:'side',name:'侧视',title:mode.value==='schedule'?'产线 × 订单':'星期 × 产线'}]);
const viewCaption=computed(()=>{
  const view=views.value.find(v=>v.id===activeView.value);
  if(!view)return '自由视角 · 旋转至平面附近，松手自动吸附';
  const collapsed={schedule:{top:'订单',front:'产线',side:'日期'},calendar:{top:'产线',front:'星期',side:'周次'}}[mode.value][activeView.value];
  return `${view.name}：${view.title} · ${collapsed}沿视线叠放，可筛选后查看`;
});
const unfinished=snapshot.orders.filter(o=>o.productionStatus!=='completed');
const stages=[...new Set(snapshot.lines.map(l=>l.stage))];
const availableLines=computed(()=>snapshot.lines.filter(l=>!filter.value.stage||l.stage===filter.value.stage));
const selection=computed(()=>selectProduction(snapshot,filter.value));
const counts=computed(()=>summarize(selection.value));
const invalidRange=computed(()=>!Number.isFinite(dayNumber(start.value))||!Number.isFinite(dayNumber(end.value))||start.value>end.value||dayNumber(end.value)-dayNumber(start.value)>365);
const visibleTasks=computed(()=>selection.value.tasks.filter(t=>t.date>=start.value&&t.date<=end.value));
const outsideCount=computed(()=>selection.value.tasks.length-visibleTasks.value.length);
const model=computed(()=>makeProductionScene(snapshot,mode.value,filter.value,{start:invalidRange.value?snapshot.defaultStart:start.value,end:invalidRange.value?snapshot.defaultEnd:end.value,spacing:spacing.value,dateSpacing:dateSpacing.value,lineSpacing:lineSpacing.value}));
const missingHoursDays=computed(()=>model.value.cells.filter(c=>c.kind==='day'&&c.count>0&&c.load.status==='unknown').length);
const selectedOrder=computed(()=>snapshot.orders.find(o=>o.id===selected.value?.task?.orderId));
const detailTasks=computed(()=>selected.value?.kind==='day'?selection.value.tasks.filter(t=>t.lineId===selected.value.lineId&&t.date===selected.value.date):mode.value==='schedule'?visibleTasks.value:selection.value.tasks);
const calendarLegend=[{text:'无样本数据',color:'#e8edf3'},{text:'有排程 · 工时未知',color:'#7496cc'},{text:'无所选任务 · 工时未知',color:'#dbe5f2'},{text:'无可用工时',color:'#d4d8df'},{text:'0%',color:'#d8eceb'},{text:'0–50%',color:'#82c7be'},{text:'50–80%',color:'#3aaba5'},{text:'80–100%',color:'#d9a14b'},{text:'超负载',color:'#ce665a'}];
const selectedMaterialDetails=computed(()=>selected.value?.task?taskDetails(snapshot,selected.value.task).filter(([name])=>['订单规格','部件品名','物料名称','物料编码','物料规格','计划占用工时'].includes(name)):[]);
const lineName=id=>snapshot.lines.find(l=>l.id===id)?.name||id;
function clearFilters(){filter.value={scope:'all',stage:'',orderId:'',lineId:'',date:''};selected.value=null;start.value=snapshot.defaultStart;end.value=snapshot.defaultEnd;}
function showAllDates(){const dates=selection.value.tasks.map(t=>t.date).sort();start.value=dates[0]||snapshot.defaultStart;end.value=dates.at(-1)||snapshot.defaultEnd;}
function switchMode(value){mode.value=value;selected.value=null;}
function selectCell(cell){selected.value=cell;panel.value='tasks';}
function inspectDate(date){const lineId=filter.value.lineId||selection.value.lines[0]?.id;const cell=model.value.cells.find(c=>c.date===date&&c.lineId===lineId);if(cell)selectCell(cell);}
function jumpToDay(){const cell=selected.value;filter.value.lineId=cell.lineId;start.value=cell.date;end.value=cell.date;mode.value='schedule';selected.value=null;panel.value='tasks';}
function askOrder(id){emit('ask',`请查看订单 ${id} 的生产计划、跨产线安排和待排情况（示例快照 ${snapshot.id}）。`);}
watch([()=>filter.value.scope,()=>filter.value.stage,()=>filter.value.lineId,()=>filter.value.orderId,start,end],()=>{selected.value=null;});
</script>
