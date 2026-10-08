<template>
  <article class="aps-card alignment-card" :class="{ compact }">
    <header class="aps-card-header">
      <div><small>{{ spec.eyebrow }}</small><h3>{{ block.title }}</h3></div>
      <div class="aps-card-actions">
        <span>{{ spec.sourceLabel }}</span>
        <button v-if="compact" type="button" @click="openDetail('unlinked-plan')">查看详细 <b>↗</b></button>
      </div>
    </header>

    <p class="aps-headline">{{ spec.headline }}</p>
    <div class="aps-snapshot">
      快照 {{ spec.snapshotId }} · 取数时间 {{ formatTime(spec.asOf) }}
      <template v-if="spec.range"> · 范围 {{ spec.range.start }} 至 {{ spec.range.end }}</template>
      <template v-if="spec.lineId"> · 产线 {{ spec.lineId }}</template>
    </div>

    <section class="aps-metrics alignment-metrics" aria-label="计划任务关键指标">
      <button v-for="(metric, index) in spec.metrics || []" :key="metric.label" type="button" :disabled="!metric.available" :class="`metric-${index + 1}`" @click="openDetail(metric.filter)">
        <span class="alignment-metric-label">{{ metric.label }}</span>
        <strong :class="{ unknown: !metric.available }">{{ metric.value }}<small>{{ metric.unit }}</small></strong>
      </button>
    </section>

    <template v-if="!compact">
      <section class="alignment-charts">
        <div class="alignment-chart-panel">
          <div class="aps-section-heading"><div><i></i><strong>生产计划任务生成情况</strong></div><small>按生产计划主键去重</small></div>
          <PlanTaskFlowChart v-if="spec.planFlow?.available" :flow="spec.planFlow" @select="openDetail" />
          <div v-else class="aps-empty">当前查询未限定生产计划范围，暂无法确认。</div>
        </div>
        <div class="alignment-chart-panel">
          <div class="aps-section-heading"><div><i></i><strong>当前任务状态分布</strong></div><small>点击柱形查看对应明细</small></div>
          <TaskStatusByLineChart v-if="lineStatus.length" :rows="lineStatus" @select="openDetail" />
          <div v-else class="aps-empty">当前范围没有任务状态记录。</div>
        </div>
      </section>

      <section class="alignment-detail aps-detail">
        <div class="aps-section-heading">
          <div><i></i><strong>{{ tableTitle }}</strong></div>
          <button v-if="rows.length > 10" class="alignment-more" type="button" @click="showAll = !showAll">{{ showAll ? '收起' : '查看全部' }}</button>
          <small v-else>{{ rows.length }} 条</small>
        </div>
        <div class="alignment-filter-chips">
          <button v-for="item in filters" :key="item.key" type="button" :class="{ active: activeFilter === item.key }" @click="selectFilter(item.key)">{{ item.label }}</button>
        </div>
        <div v-if="!rows.length" class="aps-empty">当前筛选没有记录。</div>
        <div v-else class="aps-table-wrap">
          <table>
            <thead><tr><th>类型</th><th>单号</th><th>产品</th><th>产线</th><th>开始日期</th><th>结束日期</th><th>数量</th><th>任务生成情况</th><th>说明</th></tr></thead>
            <tbody><tr v-for="row in visibleRows" :key="`${row.rowType}-${row.id}`">
              <td>{{ row.rowType === 'plan' ? '生产计划' : '当前任务' }}</td><td>{{ row.number || row.id }}</td><td>{{ row.product || row.productNo || '—' }}</td>
              <td>{{ row.line || '—' }}</td><td>{{ row.start || '—' }}</td><td>{{ row.end || '—' }}</td><td>{{ formatCell(row.quantity) }}</td>
              <td :class="{ danger: row.status?.startsWith('暂未') }">{{ row.status || '—' }}</td><td>{{ row.reason || '—' }}</td>
            </tr></tbody>
          </table>
        </div>
      </section>

      <section class="alignment-data-check">
        <div><strong>数据核对提示</strong><p>{{ spec.dataCheck?.message || '当前没有需要核对的来源计划记录。' }}</p></div>
        <button v-if="spec.dataCheck?.unmatchedSourceTaskCount" type="button" @click="openDetail('orphan')">查看待核对记录</button>
      </section>
      <footer class="aps-boundary"><strong>数据与能力边界</strong><span v-for="item in spec.limitations || []" :key="item">{{ item }}</span></footer>
    </template>
  </article>
</template>

<script setup>
import { computed, ref, watch } from 'vue';
import PlanTaskFlowChart from './PlanTaskFlowChart.vue';
import TaskStatusByLineChart from './TaskStatusByLineChart.vue';
import { alignmentFilterLabel, buildTaskStatusByLine, selectAlignmentRows } from '../services/plan-task-alignment.js';

const props = defineProps({ block: { type: Object, required: true }, compact: { type: Boolean, default: false }, initialFilter: { type: String, default: 'unlinked-plan' } });
const emit = defineEmits(['open']);
const spec = computed(() => props.block.spec || {});
const activeFilter = ref(props.initialFilter === 'all' ? 'unlinked-plan' : props.initialFilter);
const showAll = ref(false);
watch(() => props.initialFilter, value => { activeFilter.value = value === 'all' ? 'unlinked-plan' : (value || 'unlinked-plan'); showAll.value = false; });
const rows = computed(() => selectAlignmentRows(spec.value, activeFilter.value));
const visibleRows = computed(() => showAll.value ? rows.value : rows.value.slice(0, 10));
const lineStatus = computed(() => buildTaskStatusByLine(spec.value));
const activeLabel = computed(() => alignmentFilterLabel(activeFilter.value));
const tableTitle = computed(() => `${activeLabel.value}${activeFilter.value === 'unlinked-plan' && !showAll.value ? '（前10条）' : ''}`);
const filters = [
  { key: 'unlinked-plan', label: '暂未生成任务' }, { key: 'linked', label: '已生成任务' },
  { key: 'run', label: '运行中' }, { key: 'finish', label: '已完成' }, { key: 'close', label: '已关闭' },
  { key: 'orphan', label: '待核对来源' },
];
function selectFilter(filter) { activeFilter.value = filter; showAll.value = false; }
function openDetail(filter) {
  if (!props.compact) { selectFilter(filter || 'unlinked-plan'); return; }
  emit('open', { id: `alignment-${spec.value.snapshotId}-${filter}`, type: 'plan-task-alignment', title: props.block.title, block: props.block, filter });
}
function formatCell(value) { return value === null || value === undefined || value === '' ? '—' : Number(value).toLocaleString('zh-CN'); }
function formatTime(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }).format(date) + '（北京时间）';
}
</script>
