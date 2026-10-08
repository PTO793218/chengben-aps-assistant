<template><div ref="el" class="alignment-chart-canvas" role="img" aria-label="生产计划任务生成情况图"></div></template>

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
  const linked = Number(props.flow?.linked || 0);
  const unlinked = Number(props.flow?.unlinked || 0);
  const percent = value => total ? `${(value / total * 100).toFixed(2)}%` : '0%';
  const nodes = [
    { name: '生产计划', value: total, filter: 'all', itemStyle: { color: '#5b9cf2' }, labelText: `生产计划\n${total.toLocaleString('zh-CN')}` },
    { name: '已找到当前任务的计划', value: linked, filter: 'linked', itemStyle: { color: '#4f9bf5' }, labelText: `已找到当前任务的计划\n${linked.toLocaleString('zh-CN')}（${percent(linked)}）` },
    { name: '暂未找到当前任务的计划', value: unlinked, filter: 'unlinked-plan', itemStyle: { color: '#f3a04b' }, labelText: `暂未找到当前任务的计划\n${unlinked.toLocaleString('zh-CN')}（${percent(unlinked)}）` },
  ];
  const links = [
    linked > 0 && { source: '生产计划', target: '已找到当前任务的计划', value: linked, lineStyle: { color: '#9bc7f5', opacity: .46 } },
    unlinked > 0 && { source: '生产计划', target: '暂未找到当前任务的计划', value: unlinked, lineStyle: { color: '#f4c696', opacity: .5 } },
  ].filter(Boolean);
  chart.setOption({
    animation: false,
    tooltip: { trigger: 'item', formatter: p => p.dataType === 'edge' ? `${p.data.source} → ${p.data.target}<br/>${Number(p.data.value).toLocaleString('zh-CN')} 个计划` : p.data.labelText },
    series: [{ type: 'sankey', left: 12, right: 190, top: 22, bottom: 18, nodeWidth: 12, nodeGap: 24, layoutIterations: 0, data: nodes, links, draggable: false, emphasis: { focus: 'adjacency' }, lineStyle: { curveness: .48 }, label: { color: '#38546d', fontSize: 11, lineHeight: 18, formatter: p => p.data.labelText }, itemStyle: { borderWidth: 0, borderRadius: 3 } }],
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
