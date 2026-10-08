<template>
  <section class="line-load-panel">
    <header class="load-heading">
      <div><small>CAPACITY · 关联产线</small><h4>这些安排占了多少产能？</h4></div>
      <div class="load-controls" v-if="load.available">
        <label>粒度<select aria-label="负载粒度" v-model="grain"><option value="year">年</option><option value="month">月</option><option value="day">日</option></select></label>
        <label>统计期间<select aria-label="负载统计期间" v-model="period"><option v-for="p in periods" :key="p" :value="p">{{ p }}</option></select></label>
      </div>
    </header>
    <p v-if="!load.available" class="load-note">{{ load.summary }} 请刷新包含工作日历的真实快照。</p>
    <template v-else>
      <p class="load-note">{{ period }}{{ grain === 'month' ? ' 整月' : grain === 'year' ? ' 全年' : ' 当日' }} · 同版本整线计划，包含其他订单 · 与上方甘特日期范围独立</p>
      <div class="load-insights">
        <div><span>可计算产线最高负载</span><strong>{{ highest ? percent(highest.loadPercent) : '暂不可算' }}</strong><small>{{ highest?.lineName || '理论工时不足以计算' }}</small></div>
        <div :class="{ attention: overloaded > 0 }"><span>超载 / 停工仍排程</span><strong>{{ overloaded }} <em>产线日</em></strong><small>检查每日峰值，避免月均掩盖拥挤</small></div>
        <div><span>整线换型占用</span><strong>{{ changeShare }}<em v-if="changeShare !== '—'">%</em></strong><small>换型工时占全部已排工时</small></div>
        <div :class="{ attention: missing > 0 }"><span>缺失 / 无效日历</span><strong>{{ missing }} <em>产线日</em></strong><small>缺失不按停工处理，不补理论工时</small></div>
      </div>
      <p class="load-scroll-hint">图表与明细可横向滑动查看</p>
      <div class="load-chart-scroll"><div ref="comparisonEl" class="load-chart" role="img" :aria-label="`${period}各产线理论工时、整线已排工时和本单占用对比`"></div></div>
      <div class="load-table-wrap">
        <table class="load-table">
          <thead><tr><th>相关产线</th><th>理论工时</th><th>整线已排</th><th>其中本单</th><th>负载率</th><th>余量 / 超出</th><th>日历与排程检查</th></tr></thead>
          <tbody><tr v-for="r in rows" :key="r.lineId" :class="{ selected: r.lineId === selectedLine }">
            <td><button type="button" @click="selectedLine = r.lineId" :aria-pressed="r.lineId === selectedLine">{{ r.lineName }}<small>{{ r.lineId }}</small></button></td>
            <td>{{ hours(r.capacityHours) }}</td><td>{{ hours(r.plannedHours) }}<small>生产 {{ hours(r.madeHours) }} · 换型 {{ hours(r.changeHours) }}</small></td>
            <td>{{ hours(r.orderHours) }}<small>生产 {{ hours(r.orderMadeHours) }} · 换型 {{ hours(r.orderChangeHours) }}</small></td>
            <td :class="{ warning: r.loadPercent > 100 }">{{ percent(r.loadPercent) }}</td>
            <td :class="{ warning: r.remainingHours < 0 }">{{ r.remainingHours === null ? '—' : r.remainingHours < 0 ? `超出 ${hours(-r.remainingHours)}` : `余 ${hours(r.remainingHours)}` }}</td>
            <td class="load-quality">{{ quality(r) }}</td>
          </tr></tbody>
        </table>
      </div>
      <template v-if="grain !== 'day'">
        <div class="load-trend-heading"><strong>{{ selected?.lineName }} · {{ grain === 'year' ? '逐月' : '逐日' }}工时对照</strong><span>点击上表产线切换；虚线是理论工时</span></div>
        <div class="load-chart-scroll"><div ref="trendEl" class="load-trend" role="img" :aria-label="`${selected?.lineName}逐期工时和日历对照`"></div></div>
        <div class="load-exceptions" v-if="exceptions.length"><strong>需要核对的时段 · 点击查看该期</strong><button v-for="r in exceptions" :key="r.periodKey" type="button" @click="inspectPeriod(r)">{{ r.periodKey }}：{{ quality(r) }}{{ r.remainingHours < 0 ? `，超出 ${hours(-r.remainingHours)}` : '' }}</button></div>
      </template>
      <details class="load-method"><summary>计算口径与来源</summary><p>{{ load.note }}</p><p>计划：TA06_produce_plan_detail_active；日历：TA05_work_calendar_detail。仅比较当前版本已排计划，不表示全部业务承诺。零理论工时的负载率显示“—”；缺失日历或工时的汇总率也显示“—”。超载按工时比值标记，炉线并行/批量加工约束尚未核实，不能据此断言延期。余量不等于可直接插单。</p></details>
    </template>
  </section>
</template>

<script setup>
import * as echarts from 'echarts/core';
import { BarChart, LineChart } from 'echarts/charts';
import { GridComponent, TooltipComponent, LegendComponent } from 'echarts/components';
import { CanvasRenderer } from 'echarts/renderers';
import { computed, nextTick, onMounted, onBeforeUnmount, ref, watch } from 'vue';
echarts.use([BarChart, LineChart, GridComponent, TooltipComponent, LegendComponent, CanvasRenderer]);
const props = defineProps({ load: { type: Object, required: true } });
const grain = ref('month');
const period = ref(props.load.defaultPeriod);
const initial = props.load.periods.filter(r => r.grain === 'month' && r.periodKey === props.load.defaultPeriod && r.loadPercent !== null).sort((a,b) => b.loadPercent-a.loadPercent);
const selectedLine = ref(initial[0]?.lineId || props.load.periods?.[0]?.lineId || '');
const periods = computed(() => [...new Set(props.load.periods.filter(r => r.grain === grain.value).map(r => r.periodKey))].sort());
const rows = computed(() => props.load.periods.filter(r => r.grain === grain.value && r.periodKey === period.value));
const selected = computed(() => rows.value.find(r => r.lineId === selectedLine.value) || rows.value[0]);
const highest = computed(() => rows.value.filter(r => r.loadPercent !== null).sort((a,b) => b.loadPercent-a.loadPercent)[0]);
const overloaded = computed(() => rows.value.reduce((s,r) => s+r.overloadedDays+r.zeroCapacityPlanDays,0));
const missing = computed(() => rows.value.reduce((s,r) => s+r.missingCalendarDays+r.invalidCalendarDays,0));
const changeShare = computed(() => {
  if (rows.value.some(r => r.plannedHours === null)) return '—';
  const total = rows.value.reduce((s,r) => s+r.plannedHours,0);
  return total > 0 ? number(rows.value.reduce((s,r) => s+r.changeHours,0)/total*100) : '—';
});
const trendRows = computed(() => props.load.periods.filter(r => r.lineId === selected.value?.lineId && r.grain === (grain.value === 'year' ? 'month' : 'day') && r.periodKey.startsWith(period.value)).sort((a,b) => a.periodKey.localeCompare(b.periodKey)));
const exceptions = computed(() => trendRows.value.filter(r => r.missingCalendarDays || r.invalidCalendarDays || r.overloadedDays || r.zeroCapacityPlanDays || r.missingPlanHours));
function number(n) { return new Intl.NumberFormat('zh-CN',{maximumFractionDigits:2}).format(n); }
function hours(n) { return n === null || n === undefined ? '—' : `${number(n)} h`; }
function percent(n) { return n === null || n === undefined ? '—' : `${number(n)}%`; }
function inspectPeriod(r) { grain.value=r.grain;period.value=r.periodKey; }
function quality(r) {
  const notes=[];
  if(r.missingCalendarDays) notes.push(`缺日历 ${r.missingCalendarDays} 天`);
  if(r.invalidCalendarDays) notes.push(`无效日历 ${r.invalidCalendarDays} 天`);
  if(r.missingPlanHours) notes.push(`缺工时 ${r.missingPlanHours} 条`);
  if(r.overloadedDays) notes.push(`超载 ${r.overloadedDays} 天`);
  if(r.zeroCapacityPlanDays) notes.push(`零产能仍排 ${r.zeroCapacityPlanDays} 天`);
  if(r.nonworkingDays) notes.push(`无工作工时 ${r.nonworkingDays} 天`);
  return notes.join(' · ') || '日历完整';
}
watch(grain, () => {
  const old=period.value;
  const prefix=grain.value === 'year' ? old.slice(0,4) : grain.value === 'month' ? old.slice(0,7) : old;
  period.value=periods.value.find(p => p === prefix) || periods.value.find(p => p.startsWith(prefix)) || periods.value[0];
});
const comparisonEl=ref(), trendEl=ref();
let comparison, trend, observer;
const base = { animation:false, tooltip:{trigger:'axis',renderMode:'richText',axisPointer:{type:'shadow'},valueFormatter:v=>v == null ? '—' : `${number(v)} h`}, legend:{top:0,textStyle:{fontSize:10}}, grid:{left:58,right:20,top:65,bottom:40}, yAxis:{type:'value',name:'工时 h',splitLine:{lineStyle:{color:'#edf1f6'}}} };
async function draw() {
  await nextTick();
  if(!props.load.available || !comparisonEl.value) return;
  comparison ||= echarts.init(comparisonEl.value);
  const items=rows.value;
  const bar=(name,color,values,stack) => ({name,type:'bar',stack,barMaxWidth:34,itemStyle:{color},data:values});
  comparison.setOption({...base,xAxis:{type:'category',data:items.map(r=>r.lineName),axisLabel:{interval:0,width:100,overflow:'truncate'}},series:[
    bar('理论工时','#cfdaeb',items.map(r=>r.capacityHours)),
    bar('其他订单生产','#8aa7cf',items.map(r=>r.madeHours === null || r.orderMadeHours === null ? null : r.madeHours-r.orderMadeHours),'load'),
    bar('其他订单换型','#e8cda0',items.map(r=>r.changeHours === null || r.orderChangeHours === null ? null : r.changeHours-r.orderChangeHours),'load'),
    bar('本单生产','#347fee',items.map(r=>r.orderMadeHours),'load'),
    bar('本单换型','#ed9851',items.map(r=>r.orderChangeHours),'load')
  ]},true);
  observer?.observe(comparisonEl.value);
  if(trendEl.value) {
    trend ||= echarts.init(trendEl.value);
    const t=trendRows.value;
    trend.setOption({...base,grid:{left:58,right:20,top:50,bottom:40},xAxis:{type:'category',data:t.map(r=>r.periodKey.slice(grain.value === 'year' ? 5 : 8)),axisLabel:{interval:'auto'}},series:[
      bar('整线已排','#8baacf',t.map(r=>({value:r.plannedHours,itemStyle:{color:r.remainingHours < 0 ? '#dc8066' : '#8baacf'}}))),
      {name:'理论工时',type:'line',connectNulls:false,symbolSize:4,lineStyle:{type:'dashed',color:'#4d696b'},itemStyle:{color:'#4d696b'},data:t.map(r=>r.capacityHours)},
      {name:'其中本单',type:'line',symbolSize:5,itemStyle:{color:'#347fee'},data:t.map(r=>r.orderHours)}
    ]},true);
    observer?.observe(trendEl.value);
  } else { trend?.dispose(); trend=null; }
  comparison.resize();trend?.resize();
}
onMounted(()=>{observer=new ResizeObserver(()=>{comparison?.resize();trend?.resize();});draw();});
watch([rows,selected,grain],draw);
onBeforeUnmount(()=>{observer?.disconnect();comparison?.dispose();trend?.dispose();comparison=null;trend=null;});
</script>

<style scoped>
.line-load-panel{margin-top:26px;padding:20px;border:1px solid #dce7f2;border-radius:12px;background:linear-gradient(145deg,#f8fbff,#fff 40%)}
.load-heading{display:flex;justify-content:space-between;align-items:center;gap:16px}.load-heading small{color:#758ba7;letter-spacing:1px;font-size:9px}.load-heading h4{margin:6px 0;font-size:17px;color:#294d76}.load-controls{display:flex;gap:12px;flex-wrap:wrap}.load-controls label{display:flex;gap:6px;align-items:center;font-size:11px;color:#75869a}.load-controls select{padding:6px 8px;border:1px solid #d5e0ed;border-radius:6px;background:#fff;color:#37597e;max-width:155px}
.load-note{font-size:11px;color:#798da4;line-height:1.7;margin:10px 0 16px}.load-insights{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px}.load-insights>div{display:flex;flex-direction:column;gap:8px;padding:13px;border-radius:8px;background:#f0f5fb;color:#647b94}.load-insights span{font-size:11px}.load-insights strong{font-size:24px;color:#345d8a;font-weight:600}.load-insights em{font-size:10px;font-style:normal;font-weight:400}.load-insights small{font-size:10px;line-height:1.5}.load-insights .attention{background:#fff4ed}.load-insights .attention strong{color:#b06b48}.load-chart{height:310px;width:100%;margin-top:18px}.load-trend{height:240px;width:100%}
.load-exceptions button{border:0;background:transparent;text-align:left;color:inherit;font:inherit;cursor:pointer;padding:4px}.load-exceptions button:hover{background:#ffefdf}.load-exceptions button:focus-visible{outline:2px solid #c87e52}
.load-chart-scroll{max-width:100%;overflow-x:auto}.load-chart,.load-trend{min-width:520px}.load-scroll-hint{display:none;font-size:10px;color:#8598ad}
.load-table-wrap{overflow:auto;border:1px solid #e0e8f0;border-radius:8px}.load-table{border-collapse:collapse;min-width:840px;width:100%;font-size:11px;text-align:left}.load-table th{padding:10px;background:#f1f5fa;white-space:nowrap;color:#71859a;font-weight:500}.load-table td{padding:12px 10px;border-top:1px solid #e6edf5;font-variant-numeric:tabular-nums;color:#405e7e}.load-table small{display:block;font-size:9px;margin-top:5px;color:#8b9aad}.load-table .selected{background:#f1f7ff}.load-table button{border:0;background:transparent;color:#3476bd;text-align:left;cursor:pointer;font-size:11px}.load-table button:focus-visible{outline:2px solid #347fee}.load-table .warning{color:#c5674e;font-weight:600}.load-quality{max-width:170px;line-height:1.6}.load-trend-heading{display:flex;justify-content:space-between;gap:10px;margin:22px 0 10px;color:#426182;font-size:12px}.load-trend-heading span{font-size:10px;color:#8295aa}.load-exceptions{display:flex;flex-direction:column;gap:7px;max-height:145px;overflow:auto;padding:12px;background:#fff8f2;font-size:11px;color:#a4714c;border-radius:7px}.load-method{margin-top:16px;font-size:10px;color:#7c8fa5;line-height:1.8}.load-method summary{cursor:pointer}.load-method p{margin:8px 0}
@media(max-width:720px){.line-load-panel{padding:12px}.load-heading{align-items:flex-start;flex-direction:column}.load-insights{grid-template-columns:repeat(2,minmax(0,1fr))}.load-chart{height:350px}.load-trend-heading{flex-direction:column}.load-scroll-hint{display:block}}
</style>
