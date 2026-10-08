<template>
  <article class="aps-card alignment-card order-dispatch-card" :class="{ compact }">
    <header class="aps-card-header">
      <div><small>{{ spec.eyebrow }}</small><h3>{{ block.title }}</h3></div>
      <div class="aps-card-actions">
        <span>{{ spec.sourceLabel }}</span>
        <button v-if="compact" type="button" @click="openDetail('all-orders')">查看详细 <b>↗</b></button>
      </div>
    </header>

    <p class="aps-headline">{{ spec.headline }}</p>
    <div class="aps-snapshot">
      快照 {{ spec.snapshotId }} · 取数时间 {{ formatTime(spec.asOf) }}
      <template v-if="spec.lineId"> · 产线 {{ spec.lineId }}</template>
      <template v-if="spec.orderId"> · 订单 {{ spec.orderId }}</template>
    </div>

    <section class="aps-metrics alignment-metrics" aria-label="订单派工关键指标">
      <button v-for="(metric, index) in spec.metrics || []" :key="metric.label" type="button" :disabled="!metric.available" :class="'metric-' + (index + 1)" @click="openDetail(metric.filter)">
        <span class="alignment-metric-label">{{ metric.label }}</span>
        <strong :class="{ unknown: !metric.available }">{{ metric.value }}<small>{{ metric.unit }}</small></strong>
      </button>
    </section>

    <template v-if="!compact">
      <section class="alignment-charts">
        <div class="alignment-chart-panel">
          <div class="aps-section-heading"><div><i></i><strong>订单到派工状态关系</strong></div><small>点击节点查看订单明细</small></div>
          <OrderDispatchFlowChart v-if="spec.orderFlow?.available" :flow="spec.orderFlow" @select="openDetail" />
          <div v-else class="aps-empty">当前查询没有可用订单关系。</div>
        </div>
        <div class="alignment-chart-panel">
          <div class="aps-section-heading"><div><i></i><strong>按产线统计任务状态</strong></div><small>点击柱形查看任务明细</small></div>
          <TaskStatusByLineChart v-if="lineStatus.length" :rows="lineStatus" @select="openDetail" />
          <div v-else class="aps-empty">当前范围没有任务状态记录。</div>
        </div>
      </section>

      <section class="alignment-detail aps-detail">
        <div class="aps-section-heading">
          <div><i></i><strong>{{ tableTitle }}</strong></div>
          <button v-if="activeLine" class="alignment-more" type="button" aria-label="清除产线筛选" @click="selectFilter(activeFilter)">清除产线筛选</button>
          <button v-if="rows.length > 10" class="alignment-more" type="button" @click="showAll = !showAll">{{ showAll ? '收起' : '查看全部' }}</button>
          <small v-else>{{ rows.length }} 条</small>
        </div>
        <div class="alignment-filter-chips">
          <button v-for="item in filters" :key="item.key" type="button" :class="{ active: activeFilter === item.key }" @click="selectFilter(item.key)">{{ item.label }}</button>
        </div>
        <div v-if="!rows.length" class="aps-empty">当前筛选没有记录。</div>
        <div v-else class="aps-table-wrap">
          <table>
            <thead><tr><th>类型</th><th>订单号</th><th>任务号</th><th>产品</th><th>产线</th><th>关联当前任务数</th><th>在制任务数</th><th>当前核对状态</th><th>核对结论</th></tr></thead>
            <tbody><tr v-for="row in visibleRows" :key="row.rowType + '-' + row.id">
              <td>{{ row.rowType === 'order' ? '订单' : '当前任务' }}</td>
              <td>{{ row.orderNo || (row.rowType === 'order' ? row.number || row.id : '—') }}</td>
              <td>{{ row.rowType === 'task' ? row.number || row.id : '—' }}</td>
              <td>{{ row.product || row.productNo || '—' }}</td>
              <td>{{ row.line || '暂无法确认' }}</td>
              <td>{{ formatCell(row.dispatchTaskCount) }}</td>
              <td>{{ formatCell(row.inProgressTaskCount) }}</td>
              <td :class="{ danger: row.risk === 'attention' }">{{ row.rowType === 'order' ? (row.status || '—') : (row.taskStatus || row.status || '—') }}</td>
              <td :class="{ danger: row.risk === 'attention' }">{{ row.reason || '—' }}</td>
            </tr></tbody>
          </table>
        </div>
      </section>

      <section class="alignment-data-check">
        <div><strong>来源核对提示</strong><p>{{ spec.dataCheck?.message || '当前没有需要核对的来源订单记录。' }}</p></div>
        <button v-if="spec.dataCheck?.unmatchedTaskCount" type="button" @click="openDetail('unmatched-task')">查看待核对任务</button>
      </section>
      <p class="order-dispatch-status-note"><strong>现场下发状态</strong>：{{ spec.releaseStatusNote || '当前快照未取得（不代表未下发）' }}</p>

      <section class="order-dispatch-risk">
        <div><strong>风险说明</strong><p>{{ spec.risk?.headline || '当前没有额外风险说明。' }}</p></div>
        <ul v-if="spec.risk?.items?.length">
          <li v-for="item in spec.risk.items" :key="item.orderNo"><b>{{ item.orderNo }}</b> · {{ item.status }} · {{ item.reason }}</li>
        </ul>
      </section>

      <footer class="aps-boundary"><strong>数据与能力边界</strong><span v-for="item in spec.limitations || []" :key="item">{{ item }}</span></footer>
    </template>
  </article>
</template>

<script setup>
import { computed, ref, watch } from 'vue';
import OrderDispatchFlowChart from './OrderDispatchFlowChart.vue';
import TaskStatusByLineChart from './TaskStatusByLineChart.vue';
import { buildTaskStatusByLine, orderDispatchFilterLabel, selectOrderDispatchRows } from '../services/order-dispatch.js';

const props = defineProps({ block: { type: Object, required: true }, compact: { type: Boolean, default: false }, initialFilter: { type: String, default: 'all-orders' }, initialLine: { type: String, default: '' } });
const emit = defineEmits(['open']);
const spec = computed(() => props.block.spec || {});
const activeFilter = ref(props.initialFilter || 'all-orders');
const activeLine = ref(props.initialLine);
const showAll = ref(false);
watch(() => [props.initialFilter, props.initialLine], ([filter, line]) => { selectFilter(filter || 'all-orders', line); });
const rows = computed(() => selectOrderDispatchRows(spec.value, activeFilter.value, activeLine.value));
const visibleRows = computed(() => showAll.value ? rows.value : rows.value.slice(0, 10));
const lineStatus = computed(() => buildTaskStatusByLine(spec.value));
const activeLabel = computed(() => orderDispatchFilterLabel(activeFilter.value));
const tableTitle = computed(() => activeLabel.value + (activeLine.value ? ' · ' + activeLine.value : '') + (rows.value.length > 10 && !showAll.value ? '（前10条）' : ''));
const filters = [
  { key: 'all-orders', label: '全部订单' },
  { key: 'dispatch-in-progress', label: '派工在制' },
  { key: 'task-not-generated', label: '未形成当前任务' },
  { key: 'dispatch-completed', label: '已完成' },
  { key: 'dispatch-closed', label: '已关闭' },
  { key: 'unmatched-task', label: '任务来源未取得' },
];
function selectFilter(filter, line = '') { activeFilter.value = filter; activeLine.value = line; showAll.value = false; }
function openDetail(filter, line = '') {
  if (!props.compact) { selectFilter(filter || 'all-orders', line); return; }
  emit('open', { id: 'order-dispatch-' + spec.value.snapshotId + '-' + filter + '-' + line, type: 'order-dispatch', title: props.block.title, block: props.block, filter, line });
}
function formatCell(value) { return value === null || value === undefined || value === '' ? '—' : Number(value).toLocaleString('zh-CN'); }
function formatTime(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }).format(date) + '（北京时间）';
}
</script>
