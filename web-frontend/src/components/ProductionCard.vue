<template>
  <section class="production-card" aria-label="生产计划概况">
    <div class="production-card-heading"><span class="production-eyebrow">PRODUCTION OVERVIEW</span><span class="production-demo">本地示例</span></div>
    <h3>从订单，看见整个生产计划</h3>
    <p class="production-meta">{{ scopeLabel }} · 快照 {{ snapshot.asOf }} · {{ snapshot.id }}</p>
    <div class="production-metrics">
      <div><strong>{{ counts.total }}</strong><span>未完工订单</span></div>
      <div><strong>{{ counts.full }}</strong><span>已排完整</span></div>
      <div><strong>{{ counts.partial }}</strong><span>部分已排</span></div>
      <div><strong>{{ counts.none }}</strong><span>尚未排入</span></div>
    </div>
    <div class="production-view-cards">
      <button v-for="mode in ['schedule','calendar']" :key="mode" class="production-view-card" :aria-label="mode==='schedule'?'查看订单排程全景':'查看产线日历全景'" @click="open(mode)">
        <div class="production-art"><ProductionPreview :snapshot="snapshot" :focus="block.spec.focus" :mode="mode"/><span class="production-expand">↗</span></div>
        <div class="production-view-title"><strong>{{ mode==='schedule'?'订单排程全景':'产线年度日历' }}</strong><span>点击查看全景 ↗</span></div>
        <small>{{ mode==='schedule'?'日期 × 产线 × 订单 · 查看跨线安排':'一线一层 · 查看全年计划分布' }}</small>
      </button>
    </div>
    <div class="production-alerts"><span>{{ counts.risk }} 个计划交期风险</span></div>
    <p v-if="!counts.total" class="production-meta">当前筛选没有匹配订单。可在全景中清除筛选。</p>
    <p class="production-footnote">排程预览 {{ snapshot.defaultStart }} — {{ snapshot.defaultEnd }}；年度日历中浅灰表示无样本数据。</p>
    <p class="production-footnote">已排完整 ≠ 已生产完成。图中数量为示例；全景支持筛选和查看，不执行改排。</p>
    <div class="production-followups"><button @click="$emit('ask','哪些订单有计划交期风险？')">哪些订单有计划交期风险？</button><button @click="$emit('ask','哪些订单还没有完整排入计划？')">查看未完整排入订单</button></div>
  </section>
</template>
<script setup>
import {computed} from 'vue';
import ProductionPreview from './ProductionPreview.vue';
import {selectProduction,summarize} from '../services/production';
const props=defineProps({block:Object});
const emit=defineEmits(['open','ask']);
const snapshot=computed(()=>props.block.spec.snapshot);
const counts=computed(()=>summarize(selectProduction(snapshot.value,props.block.spec.focus)));
const scopeLabel=computed(()=>{const f=props.block.spec.focus||{};return [f.orderId,f.lineId,f.date,f.scope==='risk'?'交期风险订单':f.scope==='pending'?'历史占位筛选':'全部未完工订单'].filter(Boolean).join(' · ');});
function open(mode){emit('open',{id:`${snapshot.value.id}-${mode}`,type:'production',title:mode==='schedule'?'订单排程全景':'产线日历全景',mode,spec:props.block.spec});}
</script>
