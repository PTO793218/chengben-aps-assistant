<template>
  <article class="aps-card" :class="{ compact }">
    <header class="aps-card-header">
      <div>
        <small>{{ spec.eyebrow }}</small>
        <h3>{{ block.title }}</h3>
      </div>
      <div class="aps-card-actions">
        <span>{{ spec.sourceLabel }}</span>
        <button v-if="compact" type="button" @click="openViewer">展开查看 <b>↗</b></button>
      </div>
    </header>

    <p class="aps-headline">{{ spec.headline }}</p>
    <div class="aps-snapshot">
      快照 {{ spec.snapshotId }} · 取数时间 {{ formatTime(spec.asOf) }}
      <template v-if="spec.range"> · 范围 {{ spec.range.start }} 至 {{ spec.range.end }}</template>
    </div>

    <section class="aps-metrics" aria-label="关键指标">
      <div v-for="metric in spec.metrics || []" :key="metric.label">
        <strong>{{ metric.value }}<small>{{ metric.unit }}</small></strong>
        <span>{{ metric.label }}</span>
      </div>
    </section>

    <section class="aps-chart-section">
      <div class="aps-section-heading">
        <div><i></i><strong>{{ spec.chart?.title }}</strong></div>
        <small>{{ spec.chart?.kind === 'gantt' ? '横向滚动查看日期、星期与班次' : '项数按工段统计' }}</small>
      </div>
      <div v-if="!chartHasData" class="aps-empty">{{ spec.chart?.emptyText }}</div>
      <div v-else-if="spec.chart?.kind === 'gantt' && compact" class="aps-gantt-preview" role="button" tabindex="0" @click="openViewer" @keydown.enter="openViewer">
        <table>
          <thead>
            <tr><th rowspan="2">订单 / 工段</th><th v-for="date in previewDates" :key="date.date" colspan="2">{{ date.date.slice(5) }}</th></tr>
            <tr><th v-for="column in previewColumns" :key="column.key">{{ column.shift.slice(0, 1) }}</th></tr>
          </thead>
          <tbody>
            <tr v-for="(row, rowIndex) in previewRows" :key="row.key">
              <td>{{ row.orderId }} · {{ row.stage }}</td>
              <td v-for="column in previewColumns" :key="column.key">
                <span v-for="item in ganttItems(rowIndex, column.index)" :key="item.id" :class="`tone-${item.tone}`">{{ item.label ?? item.quantity }}</span>
              </td>
            </tr>
          </tbody>
        </table>
        <div class="aps-preview-cover"><strong>展开完整排程甘特</strong><span>查看全部业务列、日期、星期与班次</span></div>
      </div>
      <div v-else-if="spec.chart?.kind === 'gantt'" class="aps-gantt-wrap">
        <table class="aps-gantt" :style="{ width: `${ganttWidth}px`, minWidth: `${ganttWidth}px` }">
          <colgroup><col v-for="column in fixedColumns" :key="column.key" :style="{ width: `${column.width}px` }"><col v-for="column in spec.chart.columns" :key="column.key" style="width:64px"></colgroup>
          <thead>
            <tr>
              <th v-for="column in fixedColumns" :key="column.key" rowspan="3" class="aps-gantt-fixed" :style="{ left: `${column.left}px` }">{{ column.label }}</th>
              <th v-for="date in ganttDates" :key="date.date" colspan="2" class="aps-gantt-date">{{ date.date }}</th>
            </tr>
            <tr><th v-for="date in ganttDates" :key="date.date" colspan="2">{{ date.weekday }}</th></tr>
            <tr><th v-for="column in spec.chart.columns" :key="column.key" class="aps-gantt-shift">{{ column.shift }}</th></tr>
          </thead>
          <tbody>
            <tr v-for="(row, rowIndex) in spec.chart.rows" :key="row.key">
              <td v-for="column in fixedColumns" :key="column.key" class="aps-gantt-fixed" :style="{ left: `${column.left}px` }" :title="formatCell(row[column.key])">{{ formatCell(row[column.key]) }}</td>
              <td v-for="(_, columnIndex) in spec.chart.columns" :key="columnIndex" class="aps-gantt-slot">
                <span v-for="item in ganttItems(rowIndex, columnIndex)" :key="item.id" :class="`tone-${item.tone}`" :title="ganttTitle(item)">{{ item.label ?? item.quantity }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-else ref="chartEl" class="aps-chart" :style="chartStyle"></div>
    </section>

    <LineLoadPanel v-if="spec.lineLoad" :load="spec.lineLoad" />

    <section v-if="!compact && spec.detail?.rows?.length" class="aps-detail">
      <div class="aps-section-heading">
        <div><i></i><strong>{{ spec.detail.title }}</strong></div>
        <small>{{ spec.detail.rows.length }} 条</small>
      </div>
      <div class="aps-table-wrap">
        <table>
          <thead><tr><th v-for="column in spec.detail.columns" :key="column.key">{{ column.label }}</th></tr></thead>
          <tbody>
            <tr v-for="(row, index) in spec.detail.rows" :key="row.orderId + '-' + index">
              <td v-for="column in spec.detail.columns" :key="column.key" :class="{ danger: column.key === 'risk' && row[column.key] === '有' }">
                {{ formatCell(row[column.key]) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <footer v-if="!compact" class="aps-boundary">
      <strong>数据与能力边界</strong>
      <span v-for="item in spec.limitations || []" :key="item">{{ item }}</span>
    </footer>
  </article>
</template>

<script setup>
import * as echarts from "echarts/core";
import { BarChart } from "echarts/charts";
import { GridComponent, TooltipComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import LineLoadPanel from "./LineLoadPanel.vue";

echarts.use([BarChart, GridComponent, TooltipComponent, CanvasRenderer]);

const props = defineProps({ block: { type: Object, required: true }, compact: { type: Boolean, default: false } });
const emit = defineEmits(["open"]);
const chartEl = ref();
let chart;
let observer;
const spec = computed(() => props.block.spec || {});
const fixedColumns = computed(() => {
  let left = 0;
  return (spec.value.chart?.fixedColumns || [
  { key: "lineName", label: "生产线" }, { key: "productName", label: "产品名称" },
  { key: "productSpec", label: "产品规格" }, { key: "stage", label: "当前工段" },
  { key: "planQuantity", label: "计划数" }, { key: "orderId", label: "订单号" },
  { key: "planStart", label: "计划开始日期" }, { key: "planEnd", label: "计划结束日期" },
  ]).map(column => {
    const width = ({ lineName: 105, productName: 165, productSpec: 125, stage: 65, operation: 70, planQuantity: 90, orderId: 95, planStart: 100, planEnd: 100 })[column.key] || 100;
    const result = { ...column, width, left }; left += width; return result;
  });
});
const ganttWidth = computed(() => fixedColumns.value.reduce((sum, column) => sum + column.width, 0) + (spec.value.chart?.columns?.length || 0) * 64);
const ganttDates = computed(() => (spec.value.chart?.columns || []).filter((_, index) => index % 2 === 0));
const previewColumns = computed(() => {
  const columns = spec.value.chart?.columns || [];
  const indices = (spec.value.chart?.items || []).map((item) => item.columnIndex);
  const first = indices.length ? Math.floor(Math.min(...indices) / 2) * 2 : 0;
  return columns.slice(first, first + 12).map((column, offset) => ({ ...column, index: first + offset }));
});
const previewDates = computed(() => previewColumns.value.filter((_, index) => index % 2 === 0));
const previewRows = computed(() => (spec.value.chart?.rows || []).slice(0, 6));
const ganttItemMap = computed(() => {
  const result = new Map();
  for (const item of spec.value.chart?.items || []) {
    const key = `${item.rowIndex}|${item.columnIndex}`;
    result.set(key, [...(result.get(key) || []), item]);
  }
  return result;
});
const chartHasData = computed(() => {
  const model = spec.value.chart || {};
  return model.kind === "gantt" ? !!model.items?.length : !!model.data?.length;
});
const chartStyle = computed(() => {
  const rowCount = spec.value.chart?.rows?.length || 0;
  return { height: `${Math.min(620, Math.max(300, rowCount * 30 + 110))}px` };
});

function formatCell(value) {
  if (typeof value === "number") return Number(value.toFixed(8));
  return value === null || value === undefined || value === "" ? "—" : value;
}
function formatTime(value) {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("zh-CN", { timeZone: "Asia/Shanghai", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false }).format(date) + "（北京时间）";
}
function ganttItems(rowIndex, columnIndex) { return ganttItemMap.value.get(`${rowIndex}|${columnIndex}`) || []; }
function ganttTitle(item) { return `${item.orderId} · ${item.stage}\n${item.date} ${item.shift}\n${item.operation} · ${item.lineId}\n数量 ${item.quantity} · 计划工时 ${item.plannedHours}h`; }
function openViewer() {
  emit("open", { id: `aps-${spec.value.snapshotId}-${spec.value.viewType}`, type: "aps-analysis", title: props.block.title, block: props.block });
}

function barOption(model) {
  return {
    grid: { left: 78, right: 34, top: 18, bottom: 34 },
    tooltip: { trigger: "axis", axisPointer: { type: "shadow" } },
    xAxis: { type: "value", minInterval: 1, splitLine: { lineStyle: { color: "#eef2f6" } }, axisLabel: { color: "#8290a0" } },
    yAxis: { type: "category", inverse: true, data: model.data.map((x) => x.name), axisTick: { show: false }, axisLine: { show: false }, axisLabel: { color: "#536b82" } },
    series: [{ type: "bar", data: model.data.map((x) => x.value), barMaxWidth: 24, itemStyle: { color: "#5e9bcf", borderRadius: [0, 5, 5, 0] }, label: { show: true, position: "right", color: "#63788d" } }],
  };
}

async function draw() {
  if (!chartHasData.value) { chart?.dispose(); chart = null; return; }
  await nextTick();
  if (!chartEl.value) return;
  if (spec.value.chart?.kind === "gantt") { chart?.dispose(); chart = null; return; }
  chart ||= echarts.init(chartEl.value);
  const model = spec.value.chart;
  chart.setOption(barOption(model), true);
  chart.resize();
}

onMounted(() => {
  draw();
  observer = new ResizeObserver(() => chart?.resize());
  if (chartEl.value) observer.observe(chartEl.value);
});
watch(() => props.block, draw, { deep: true });
onBeforeUnmount(() => { observer?.disconnect(); chart?.dispose(); });
</script>
