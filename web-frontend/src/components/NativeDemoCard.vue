<template>
 <article class="aps-card native-demo" :class="{compact}" aria-label="业务结果">
  <header class="aps-card-header"><div><h3>{{ title }}</h3><span v-if="phase.label&&phase.key!=='view'" class="operation-state" :class="phase.key">{{ phase.label }}</span></div><button v-if="compact" class="aps-detail-button" type="button" @click="open()">查看明细</button></header>
  <template v-if="spec.prepare">
   <p class="business-intro">{{ intro }}</p>
   <form v-if="phase.canExecute||loading" class="native-form" @submit.prevent="calculate">
    <div v-if="['reschedule','insert'].includes(spec.view)" class="native-fields">
     <OrderPicker v-if="spec.view!=='insert'||!spec.orderId" v-model="orderId" :orders="spec.view==='insert'?spec.availableOrders:(spec.allOrders||spec.orders)" :label="spec.view==='insert'?'加入计划的订单':'目标订单'" :disabled="loading" />
     <div v-else class="confirmation-scope"><span>订单</span><strong>{{ orderId }}</strong></div>
     <label v-if="spec.view!=='insert'" class="position-picker">调整到顺序<select v-model.number="position" aria-label="调整到顺序" :disabled="loading"><option v-for="n in (spec.allOrders||spec.orders).length" :key="n" :value="n">第 {{ n }} 位</option></select></label><p v-else class="native-muted">加入后放到待排顺序第 1 位</p>
    </div>
    <OrderPicker v-else-if="spec.view==='mrp'" v-model="orderId" :orders="spec.allOrders||spec.orders" label="优先查看订单" allow-all :disabled="loading" />
    <div v-else class="confirmation-scope"><span>订单范围</span><strong>当前 {{ spec.orders.length }} 张订单</strong><span>计划日期</span><strong>{{ planRange }}</strong></div>
    <p v-if="spec.view==='insert'&&selectedInsertion" class="native-muted">产品 {{ selectedInsertion.product }} · 数量 {{ num(selectedInsertion.quantity) }} 件 · 交期 {{ selectedInsertion.dueDate }}</p>
    <p v-if="spec.view==='mrp'" class="native-muted">将核算本批 {{ (spec.allOrders||spec.orders).length }} 张订单；先展示选中订单的结果。需求与供应自动读取，生产计划保持不变。</p>
    <p v-if="['reschedule','insert'].includes(spec.view)" class="native-muted">{{ spec.view==='insert'?'加入后共':'本批' }} {{ (spec.allOrders||spec.orders).length+(spec.view==='insert'?1:0) }} 张订单一起试排。先看影响，再决定是否采用；已有在制任务仍按规则安排。</p>
    <div class="native-actions"><button class="native-primary" type="submit" :disabled="loading||(['reschedule','insert'].includes(spec.view)&&!orderId)">{{ loading?'正在处理…':spec.view==='mrp'?'开始核算':spec.view==='release'?'确认下发':'开始试排' }}</button><button type="button" @click="cancel">{{ loading?'取消等待':'取消' }}</button></div>
   </form>
   <div v-else class="operation-feedback" role="status"><p>{{ status||phase.label }}</p><button v-if="phase.key==='cancelled'&&canReprepare" type="button" @click="reprepare">重新准备</button><button v-else-if="phase.key==='superseded'&&!released" type="button" @click="prepareAgain">按当前方案重新准备</button></div>
  </template>
  <template v-else>
   <p v-if="spec.archived" class="native-muted">保留查询当时的计划信息；当前操作请在新对话中进行。</p>
   <div class="native-metrics"><div v-for="metric in metrics" :key="metric.label"><strong>{{ metric.value }}</strong><span>{{ metric.label }}</span></div></div>
   <template v-if="materialView">
    <p v-if="spec.mrpPendingOrderIds?.length" class="native-muted">新加入的订单尚未完成独立物料核算，请按当前整批订单重新核算。当前计划用料可在下方对照。</p><h4>关键材料需求</h4><p class="native-muted">各项保留原计量单位；库存和在途抵扣分别核对。</p>
    <PagedDataTable :rows="focusMaterials" :columns="materialColumns" :compact="compact" label="关键材料" row-key="detailGuid" :selectable="!compact" :selected-id="selectedOrderId" :initial-query="detailQuery" search-placeholder="输入订单号或物料号" @select="selectOrder" @page-change="updatePageRows" />
    <template v-if="!compact">
     <details class="business-fold" @toggle="bomOpen=$event.target.open"><summary>查看完整需求分解（{{ spec.mrpRows.length }} 行）</summary><PagedDataTable v-if="bomOpen" :rows="spec.mrpRows" :columns="bomColumns" label="需求分解" row-key="detailGuid" :initial-query="selectedOrderId" search-placeholder="输入订单、物料、工序或层级" /></details>
     <details class="business-fold" @toggle="planMaterialsOpen=$event.target.open"><summary>对照当前计划用料（{{ spec.materialRows.length }} 项）</summary><p class="native-muted">这是当前生产计划的用料结果；保存上面的物料核算不会自动改变这些记录。</p><PagedDataTable v-if="planMaterialsOpen" :rows="spec.materialRows" :columns="planMaterialColumns" label="计划用料" row-key="detail_guid" :initial-query="selectedOrderId" search-placeholder="输入订单、物料或供应单号" /></details>
     <details class="business-fold"><summary>查看计算依据</summary><p>抵扣前需求＝剩余需求＋库存抵扣＋在途抵扣。损耗和安全需求按原核算结果逐项保留。多工序可能重复显示同一库存池，不重复累计；不同材料和单位分别核对。在途数量参与抵扣不代表已经到货。</p></details>
    </template>
   </template>
   <template v-else-if="spec.view==='dispatch'">
    <OrderPicker v-if="!compact&&spec.orders.length>1" v-model="selectedOrderId" :orders="spec.orders" label="查看订单" allow-all />
    <PagedDataTable :rows="visibleTasks" :columns="taskColumns" :compact="compact" label="派工任务" row-key="task_bill_guid" search-placeholder="输入任务号、工序或产线" />
   </template>
   <template v-else>
    <PagedDataTable v-if="spec.view!=='release'||!compact" :rows="orderRows" :columns="orderColumns" :compact="compact" label="订单明细" :selected-id="selectedOrderId" :selectable="true" :initial-query="detailQuery" :filters="orderFilters" @select="selectOrder" @page-change="updatePageRows" />
    <details v-if="!compact&&['insert','reschedule'].includes(spec.view)" class="business-fold" @toggle="changesOpen=$event.target.open"><summary>查看本次调整</summary><PagedDataTable v-if="changesOpen" :rows="spec.changes||[]" :columns="comparisonColumns" label="调整明细" :initial-query="detailQuery" /></details>
    <div v-if="!compact" class="gantt-section">
     <button class="gantt-toggle" type="button" :aria-expanded="ganttOpen" @click="ganttOpen=!ganttOpen">{{ ganttOpen?'收起工序排程':'查看工序排程' }}</button><span class="native-muted">{{ selectedOrderId?'当前订单 '+selectedOrderId:'显示当前页订单；可选中订单单独查看' }}</span><button v-if="selectedOrderId&&spec.orders.length>1" class="gantt-toggle" type="button" @click="selectedOrderId=''">查看当前页</button>
     <ApsAnalysisCard v-if="ganttOpen" :block="chartBlock" hide-metadata />
    </div>
   </template>
   <div v-if="spec.proposedPlan&&['reschedule','insert','mrp'].includes(spec.view)" class="native-actions">
    <button v-if="phase.canExecute" class="native-primary" type="button" :disabled="loading" @click="adopt">{{ spec.view==='mrp'?'保存核算结果':'采用此方案' }}</button><p v-else class="native-muted" role="status">{{ phase.label }}</p>
    <span v-if="phase.canExecute" class="native-muted">{{ spec.view==='mrp'?'保存本次核算，生产计划不变':'后续查询将使用这份计划' }}</span>
   </div>
   <div v-if="!compact&&!spec.archived" class="detail-related"><DemoWorkspaceContext :workspace="relatedWorkspace" @ask="$emit('ask',$event)" /></div>
   <div v-if="!compact&&!spec.archived&&canUndo" class="native-actions"><button type="button" :disabled="loading" @click="restore">撤销本次采用</button></div>
  </template>
  <p v-if="status&&!spec.prepare" role="status" class="native-muted">{{ status }}</p><p v-if="error" class="native-error" role="alert">{{ error }}</p>
 </article>
</template>
<script setup>
import {computed,markRaw,onBeforeUnmount,ref,toRefs,watch} from 'vue';
import ApsAnalysisCard from './ApsAnalysisCard.vue';import PagedDataTable from './PagedDataTable.vue';import OrderPicker from './OrderPicker.vue';import DemoWorkspaceContext from './DemoWorkspaceContext.vue';
import {requestDemo,samePlanBase,nativePlanKey} from '../services/demo-plan.js';
import {acquireOperation,claimOperation} from '../services/business-operations.js';
import {number as num,unitName,statusName,inProgressName,sortedOrders,materialFocusRows,operationState,businessHeadline,businessTitle,businessMetrics,scopedChart} from '../services/business-presentation.js';
const props=defineProps({block:Object,compact:Boolean,currentPlan:Object,conversationId:String,applyPlan:Function,restorePlan:Function,saveMrp:Function,focusOrderId:String,persistUi:Function,resolvePlan:Function,acceptResult:Function});
const ownerConversationId=props.conversationId;
const emit=defineEmits(['open','result','ask']);const spec=computed(()=>props.block.spec);
const operation=acquireOperation(props.block,ownerConversationId),shared=operation.state;
const {orderId,position,loading,status,error}=toRefs(shared);
const selectedOrderId=ref(props.focusOrderId||spec.value.orderId||''),pageRows=ref([]);
const cancelled=computed(()=>props.block.uiState?.cancelled===true),completed=computed(()=>props.block.uiState?.completed===true);
function saveUi(state){props.block.uiState={...props.block.uiState,...state};props.persistUi?.(ownerConversationId,props.block).catch(()=>{error.value='操作状态暂未保存，请保留当前页面。';});}
const changesOpen=ref(false),bomOpen=ref(false),planMaterialsOpen=ref(false),ganttOpen=ref(spec.value.view==='order'||!!props.focusOrderId);
const phase=computed(()=>operationState(spec.value,props.currentPlan,{cancelled:cancelled.value,loading:loading.value,completed:completed.value}));
const released=computed(()=>props.currentPlan?.releaseStatus==='released');
const canReprepare=computed(()=>!released.value&&samePlanBase(props.currentPlan,spec.value.baseState));
const canUndo=computed(()=>!released.value&&['insert','reschedule'].includes(spec.value.view)&&!!spec.value.proposedPlan&&samePlanBase(props.currentPlan,spec.value.proposedPlan));
const intro=computed(()=>businessHeadline(spec.value));
const selectedInsertion=computed(()=>(spec.value.availableOrders||[]).find(o=>o.orderId===orderId.value));
const title=computed(()=>businessTitle(spec.value));
const metrics=computed(()=>businessMetrics(spec.value.view==='dispatch'&&selectedOrderId.value?{...spec.value,orders:spec.value.orders.filter(o=>o.orderId===selectedOrderId.value),tasks:visibleTasks.value}:spec.value));const materialView=computed(()=>['material','mrp'].includes(spec.value.view));
const focusMaterials=computed(()=>materialFocusRows(spec.value.mrpRows||[]));
const detailQuery=computed(()=>props.compact?'':props.focusOrderId||'');
const planRange=computed(()=>{const starts=spec.value.orders.map(o=>o.planStart).filter(Boolean).sort(),ends=spec.value.orders.map(o=>o.planEnd).filter(Boolean).sort();return starts.length?starts[0]+' 至 '+ends.at(-1):'待核对';});
const date=value=>!value||value.startsWith('1900')?'待核对':value.slice(0,10);
const riskText=o=>o.risk?'交期有风险':'未超过交期';
const orderFilters=computed(()=>props.compact?[]:[{key:'taskTag',label:'派工在制',options:[{value:'T',label:'有在制标记'},{value:'F',label:'无在制标记'}]}]);
const orderRows=computed(()=>sortedOrders(spec.value));
const orderColumns=computed(()=>[
 {key:'orderId',label:'订单 / 产品',secondary:o=>o.product},{key:'quantity',label:'订单需求',text:o=>num(o.quantity)},{key:'taskTag',label:'派工状态',text:o=>inProgressName(o.taskTag)},...((spec.value.view==='order'&&props.compact)?[]:[{key:'dates',label:'计划日期',text:o=>date(o.planStart)+' 至 '+date(o.planEnd)},{key:'dueDate',label:'订单交期',secondary:riskText}])]);
const comparisonColumns=[{key:'orderId',label:'订单',secondary:o=>o.added?'原计划未包含':inProgressName(o.taskTag)},{key:'position',label:'顺序变化',text:o=>(o.added?'未入排':o.beforePosition)+' → '+o.afterPosition},{key:'start',label:'计划开始',text:o=>(o.added?'未入排':date(o.beforeStart))+' → '+date(o.afterStart)},{key:'end',label:'计划结束',text:o=>(o.added?'未入排':date(o.beforeEnd))+' → '+date(o.afterEnd)},{key:'dueDate',label:'交期'}];
const materialColumns=computed(()=>[{key:'orderId',label:'订单 / 物料',secondary:r=>r.material},{key:'beforeOffset',label:'抵扣前需求',text:r=>num(r.beforeOffset)+' '+unitName(r.unit)},{key:'stockUsed',label:'库存 / 在途',text:r=>'库存 '+num(r.stockUsed),secondary:r=>'在途 '+num(r.transitUsed)},{key:'remaining',label:'剩余需求',text:r=>num(r.remaining)+' '+unitName(r.unit)},...(!props.compact?[{key:'needDate',label:'需求日期',text:r=>date(r.needDate)}]:[])]);
const bomColumns=[{key:'orderId',label:'订单 / 层级',secondary:r=>r.level},{key:'material',label:'物料 / 工序',secondary:r=>r.active||'原材料'},{key:'rate',label:'单位用量',text:r=>num(r.rate)},{key:'beforeOffset',label:'抵扣前需求',text:r=>num(r.beforeOffset)+' '+unitName(r.unit)},{key:'stockUsed',label:'库存抵扣',text:r=>num(r.stockUsed)},{key:'transitUsed',label:'在途抵扣',text:r=>num(r.transitUsed)},{key:'remaining',label:'剩余需求',text:r=>num(r.remaining)},{key:'demand',label:'本工序需求',text:r=>num(r.demand)+' '+unitName(r.unit),secondary:r=>'损耗 '+num(r.scrapRate)+'% + '+num(r.scrapNum)+'；安全需求 '+num(r.safeDemand)}];
const planMaterialColumns=[{key:'sale_order_no',label:'订单 / 物料',secondary:r=>r.material_no},{key:'material_num',label:'计划用料',text:r=>num(r.material_num)+' '+unitName(r.unit_no)},{key:'this_remain_num',label:'库存占用',text:r=>num(r.this_remain_num)},{key:'this_order_num',label:'采购占用',text:r=>num(r.this_order_num)},{key:'supply_order_no',label:'供应单 / 日期',text:r=>r.supply_order_no||'未查到供应单',secondary:r=>date(r.delivery_date)}];
const taskColumns=[{key:'orderIds',label:'关联订单',text:t=>t.orderIds?.join('、')||'待核对'},{key:'task_bill_no',label:'任务号'},{key:'start_active_no',label:'工序编号 / 产线编号',text:t=>(t.start_active_no||'待核对')+' / '+(t.product_line_no||'待核对')},{key:'task_num',label:'任务量 / 已报数量',text:t=>num(t.task_num)+' / '+num(t.delivery_num)},{key:'state_type',label:'任务状态',text:t=>statusName(t.state_type)}];
const visibleTasks=computed(()=>(spec.value.tasks||[]).filter(t=>!selectedOrderId.value||!Array.isArray(t.orderIds)||t.orderIds.includes(selectedOrderId.value)));
const relatedWorkspace=computed(()=>({...spec.value.workspace,view:selectedOrderId.value&&['overview','risk','reschedule','insert','release'].includes(spec.value.view)?'order':spec.value.view,focusOrderId:selectedOrderId.value}));
const nativeColumns=[{key:'lineName',label:'产线编号'},{key:'productName',label:'产品编号'},{key:'stage',label:'工序编号'},{key:'planQuantity',label:'工序计划数'},{key:'orderId',label:'订单号'},{key:'planStart',label:'计划开始'},{key:'planEnd',label:'计划结束'}];
const chartBlock=computed(()=>{const ids=selectedOrderId.value?[selectedOrderId.value]:pageRows.value.map(o=>o.orderId);const chart=scopedChart(spec.value.chart,ids);return {title:'工序排程',spec:{headline:'按产线、日期和班次查看计划数量',metrics:[],chart:{...chart,title:'工序计划',fixedColumns:chart.fixedColumns||nativeColumns}}};});
watch(()=>props.focusOrderId,id=>{selectedOrderId.value=id||spec.value.orderId||'';});
function open(focus=''){emit('open',{id:'plan-'+spec.value.calculationId+'-'+spec.value.view,type:'interactive-demo',title:title.value,block:props.block,focusOrderId:focus});}
function selectOrder(row){if(props.compact)open(row.orderId);else{selectedOrderId.value=row.orderId;ganttOpen.value=!materialView.value;}}
function updatePageRows(rows){pageRows.value=rows;if(selectedOrderId.value&&!rows.some(row=>row.orderId===selectedOrderId.value))selectedOrderId.value='';}
function cancel(){const waiting=loading.value;saveUi({cancelled:true,pending:false});shared.controller?.abort('user');status.value=waiting?'已取消等待，当前计划没有变化。':'已取消此次操作。';}
function reprepare(){if(!canReprepare.value)return;saveUi({cancelled:false});status.value='';error.value='';}
function prepareAgain(){emit('ask',spec.value.view==='insert'?'把新订单 '+orderId.value+' 插入当前计划，放在第1位试排':spec.value.view==='mrp'?'计算'+(orderId.value?'订单 '+orderId.value:'这批订单')+'的物料需求':spec.value.view==='release'?'准备下发当前计划，先让我确认':'把订单 '+orderId.value+' 调整到第'+position.value+'位试排');}
async function calculate(){
 if(loading.value||!phase.value.canExecute)return;
 loading.value=true;error.value='';status.value='';const active=new AbortController();shared.controller=markRaw(active);saveUi({pending:true});const timer=setTimeout(()=>active.abort('timeout'),180000);let unlock;
 try{
  const plan=await props.resolvePlan();
  if(active.signal.aborted)return;
  if(!samePlanBase(plan,spec.value.baseState))throw Error('计划或物料信息已更新，请按当前方案重新准备。');
  unlock=claimOperation(JSON.stringify([nativePlanKey(plan),spec.value.view,orderId.value,position.value]));
  const result=await requestDemo({action:spec.value.view,orderId:spec.value.view==='release'?'':orderId.value,position:position.value,quantity:0,scenario:'frozen',version:spec.value.demandVersion,plan:JSON.parse(JSON.stringify(plan)),conversationId:ownerConversationId},active.signal);
  if(active.signal.aborted)return;
  const owned={...result,originConversationId:ownerConversationId};
  if(props.acceptResult)await props.acceptResult(owned,{signal:active.signal});else emit('result',owned);
  saveUi({completed:true});status.value='已完成，请查看新的结果。';
 }catch(e){if(active.signal.reason==='user')status.value='已取消等待，当前计划没有变化。';else error.value=active.signal.reason==='timeout'?'处理时间较长，请稍后重试。当前计划已保留。':e.message;}
 finally{unlock?.();clearTimeout(timer);loading.value=false;shared.controller=null;saveUi({pending:false});}
}
async function adopt(){if(loading.value||!phase.value.canExecute)return;loading.value=true;error.value='';try{await(spec.value.view==='mrp'?props.saveMrp(spec.value,ownerConversationId):props.applyPlan(spec.value,ownerConversationId));status.value=spec.value.view==='mrp'?'核算结果已保存，生产计划不变。':'方案已采用。';}catch(e){error.value=e.message;}finally{loading.value=false;}}
async function restore(){if(!canUndo.value)return;try{await props.restorePlan(spec.value,ownerConversationId);}catch(e){error.value=e.message;}}
onBeforeUnmount(operation.release);
</script>
<style scoped>
.native-demo{color:#2b405a}.native-demo .aps-card-header{align-items:flex-start;margin-bottom:18px}.native-demo h3{margin:0;font-size:20px}.operation-state{display:inline-block;margin-top:8px;color:#617792;font-size:12px}.operation-state.adopted,.operation-state.saved,.operation-state.released{color:#218267}.native-demo p{font-size:14px;line-height:1.8}.native-muted{color:#61758e;font-size:13px!important}.business-intro{margin:0 0 18px}.native-form{padding:18px;border:1px solid #dce6f2;border-radius:10px;background:#f7faff}.native-fields{display:grid;grid-template-columns:2fr 1fr;gap:18px;align-items:end}.position-picker{display:grid;gap:8px;font-size:13px}.position-picker select{height:40px;padding:8px 10px;border:1px solid #cbd8e8;border-radius:7px;background:white;color:inherit}.native-actions{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin:18px 0 0}.native-actions button,.operation-feedback button,.gantt-toggle{padding:10px 16px;background:white;border:1px solid #cad7e7;border-radius:8px;color:#315b8c;cursor:pointer;font-size:13px}.native-actions .native-primary{background:#285ca4;border-color:#285ca4;color:white}.native-actions button:disabled{opacity:.45;cursor:default}.native-metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin:0 0 18px}.native-metrics div{padding:14px;background:#f5f8fc;border-radius:9px}.native-metrics strong{font-size:20px;display:block;overflow-wrap:anywhere}.native-metrics span{font-size:13px;color:#61758e;display:block;margin-top:6px}.native-error{padding:12px;background:#fff1ef;color:#a94335;border-radius:8px}.native-demo h4{margin:18px 0 8px;font-size:15px}.business-fold{margin:16px 0;padding:14px 0;border-top:1px solid #e4ebf3;font-size:13px}.business-fold summary{cursor:pointer;color:#315c8f}.gantt-section{margin-top:18px}.gantt-section>.native-muted{margin-left:12px}.gantt-section :deep(.aps-card){margin-top:16px}.operation-feedback{padding:14px 18px;background:#f5f8fc;border-radius:9px}.operation-feedback p{margin:0 0 8px}.confirmation-scope{display:grid;grid-template-columns:90px 1fr;gap:14px;font-size:14px}.confirmation-scope span{color:#61758e}.confirmation-scope strong{font-weight:500}.detail-related{margin-top:20px}.compact{padding:22px}.compact .native-form{margin-top:8px}.native-demo button:focus-visible,.native-demo summary:focus-visible,.position-picker select:focus-visible{outline:2px solid #2c7bea;outline-offset:2px}@media(max-width:760px){.native-fields{grid-template-columns:1fr}.native-metrics{grid-template-columns:repeat(2,minmax(0,1fr))}}
</style>
