<template>
  <article class="chart-card">
    <header>
      <span><i class="blue-dot"></i>{{ block.title }}</span
      ><small>示例数据</small>
    </header>
    <div ref="el" class="chart-canvas"></div>
  </article>
</template>
<script setup>
import * as echarts from "echarts/core";
import { BarChart, LineChart, PieChart } from "echarts/charts";
import {
  GridComponent,
  TooltipComponent,
  LegendComponent,
} from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
import { onMounted, onBeforeUnmount, ref, watch } from "vue";
echarts.use([
  BarChart,
  LineChart,
  PieChart,
  GridComponent,
  TooltipComponent,
  LegendComponent,
  CanvasRenderer,
]);
const props = defineProps({ block: Object });
const el = ref();
let chart, observer;
function draw() {
  const s = props.block.spec || {},
    rows = s.data || [],
    type = s.chartType || "bar";
  const colors = [
    "#287bff",
    "#66a5ff",
    "#46c5c0",
    "#a8c9ff",
    "#98a7fa",
    "#c8ddfb",
  ];
  const option =
    type === "pie"
      ? {
          legend: { bottom: 0 },
          series: [
            {
              type: "pie",
              radius: ["43%", "70%"],
              data: rows.map((r) => ({
                name: r[s.xField],
                value: r[s.yField],
              })),
              label: { color: "#617089" },
            },
          ],
        }
      : {
          grid: { left: 45, right: 20, top: 25, bottom: 35 },
          xAxis: {
            type: "category",
            data: rows.map((r) => r[s.xField]),
            axisLine: { lineStyle: { color: "#e1e8f3" } },
            axisTick: { show: false },
            axisLabel: { color: "#8794a9" },
          },
          yAxis: {
            type: "value",
            splitLine: { lineStyle: { color: "#eef2f8" } },
            axisLabel: { color: "#8794a9" },
          },
          series: [
            {
              type: type === "line" ? "line" : "bar",
              data: rows.map((r) => r[s.yField]),
              barMaxWidth: 38,
              smooth: true,
              itemStyle: { borderRadius: type === "bar" ? [5, 5, 0, 0] : 0 },
              areaStyle: type === "line" ? { opacity: 0.07 } : undefined,
            },
          ],
        };
  chart?.setOption(
    {
      color: colors,
      tooltip: { trigger: type === "pie" ? "item" : "axis" },
      ...option,
    },
    true,
  );
}
onMounted(() => {
  chart = echarts.init(el.value);
  draw();
  observer = new ResizeObserver(() => chart?.resize());
  observer.observe(el.value);
});
watch(() => props.block, draw, { deep: true });
onBeforeUnmount(() => {
  observer?.disconnect();
  chart?.dispose();
});
</script>
