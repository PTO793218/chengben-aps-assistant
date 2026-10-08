<template><div ref="el" class="alignment-chart-canvas" role="img" aria-label="派工任务状态分布图"></div></template>

<script setup>
import * as echarts from 'echarts/core';
import { BarChart } from 'echarts/charts';
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';

echarts.use([BarChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer]);
const props = defineProps({ rows: { type: Array, default: () => [] } });
const emit = defineEmits(['select']);
const el = ref();
let chart;
let observer;
const series = [
  { name: '运行中', key: 'run', color: '#4d98f4' },
  { name: '已完成', key: 'finish', color: '#59c89a' },
  { name: '已关闭', key: 'close', color: '#9aa8c2' },
];

function draw() {
  if (!chart) return;
  chart.setOption({
    animation: false,
    color: series.map(item => item.color),
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    legend: { top: 0, right: 4, itemWidth: 9, itemHeight: 9, textStyle: { color: '#76899c', fontSize: 10 } },
    grid: { left: 42, right: 14, top: 38, bottom: 34 },
    xAxis: { type: 'category', data: props.rows.map(row => row.line), axisTick: { show: false }, axisLine: { lineStyle: { color: '#dfe7ef' } }, axisLabel: { color: '#71859a', fontSize: 10, interval: 0, overflow: 'truncate', width: 56 } },
    yAxis: { type: 'value', minInterval: 1, splitLine: { lineStyle: { color: '#eef2f6' } }, axisLine: { show: false }, axisLabel: { color: '#8a98a7', fontSize: 9 } },
    series: series.map(item => ({ name: item.name, type: 'bar', stack: 'task-status', barMaxWidth: 46, data: props.rows.map(row => row[item.key]), itemStyle: { color: item.color }, label: { show: true, position: 'inside', color: '#fff', fontSize: 9, formatter: p => p.value ? p.value : '' } })),
  }, true);
}

onMounted(() => {
  chart = echarts.init(el.value);
  chart.on('click', params => {
    const status = series.find(item => item.name === params.seriesName)?.key;
    const line = props.rows[params.dataIndex]?.line;
    if (status && line) emit('select', status, line);
  });
  draw();
  observer = new ResizeObserver(() => chart?.resize());
  observer.observe(el.value);
});
watch(() => props.rows, draw, { deep: true });
onBeforeUnmount(() => { observer?.disconnect(); chart?.dispose(); });
</script>
