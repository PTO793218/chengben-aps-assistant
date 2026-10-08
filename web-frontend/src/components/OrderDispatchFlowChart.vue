<template><div ref="el" class="alignment-chart-canvas" role="img" aria-label="订单到派工状态关系图"></div></template>

<script setup>
import * as echarts from 'echarts/core';
import { SankeyChart } from 'echarts/charts';
import { TooltipComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';

echarts.use([SankeyChart, TooltipComponent, CanvasRenderer]);
const props = defineProps({ flow: { type: Object, required: true } });
const emit = defineEmits(['select']);
const el = ref();
let chart;
let observer;

function draw() {
  if (!chart) return;
  const total = Number(props.flow?.total || 0);
  const items = [
    { name: '派工在制', value: Number(props.flow?.dispatchInProgress || 0), filter: 'dispatch-in-progress', color: '#4f9bf5' },
    { name: '已完成', value: Number(props.flow?.completed || 0), filter: 'dispatch-completed', color: '#59c89a' },
    { name: '已关闭', value: Number(props.flow?.closed || 0), filter: 'dispatch-closed', color: '#9aa8c2' },
    { name: '未形成当前任务', value: Number(props.flow?.taskNotGenerated || 0), filter: 'task-not-generated', color: '#f3a04b' },
    { name: '状态待确认', value: Number(props.flow?.unknown || 0), filter: 'unknown', color: '#c57b8d' },
  ];
  const percent = value => total ? `${(value / total * 100).toFixed(2)}%` : '0%';
  const nodes = [
    { name: '订单', value: total, filter: 'all-orders', itemStyle: { color: '#5b9cf2' }, labelText: `订单\n${total.toLocaleString('zh-CN')}` },
    ...items.filter(item => item.value > 0).map(item => ({
      name: item.name,
      value: item.value,
      filter: item.filter,
      itemStyle: { color: item.color },
      labelText: `${item.name}\n${item.value.toLocaleString('zh-CN')}（${percent(item.value)}）`,
    })),
  ];
  const links = items.filter(item => item.value > 0).map(item => ({
    source: '订单',
    target: item.name,
    value: item.value,
    lineStyle: { color: item.color, opacity: .42 },
  }));
  chart.setOption({
    animation: false,
    tooltip: { trigger: 'item', formatter: params => params.dataType === 'edge' ? `${params.data.source} → ${params.data.target}<br/>${Number(params.data.value).toLocaleString('zh-CN')} 个订单` : params.data.labelText },
    series: [{ type: 'sankey', left: 12, right: 205, top: 22, bottom: 18, nodeWidth: 12, nodeGap: 18, layoutIterations: 0, data: nodes, links, draggable: false, emphasis: { focus: 'adjacency' }, lineStyle: { curveness: .48 }, label: { color: '#38546d', fontSize: 11, lineHeight: 18, formatter: params => params.data.labelText }, itemStyle: { borderWidth: 0, borderRadius: 3 } }],
  }, true);
}

onMounted(() => {
  chart = echarts.init(el.value);
  chart.on('click', params => params.data?.filter && emit('select', params.data.filter));
  draw();
  observer = new ResizeObserver(() => chart?.resize());
  observer.observe(el.value);
});
watch(() => props.flow, draw, { deep: true });
onBeforeUnmount(() => { observer?.disconnect(); chart?.dispose(); });
</script>
