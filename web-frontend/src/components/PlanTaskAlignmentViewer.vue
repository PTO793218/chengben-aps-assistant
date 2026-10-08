<template>
  <section class="aps-analysis-viewer" aria-label="计划任务生成核对详情">
    <header>
      <div><button class="icon-button" aria-label="关闭计划任务生成核对详情" @click="$emit('close')"><Icon name="close" /></button><div><small>CHENGBEN · PLAN TASK CHECK</small><h2>{{ item.title }}</h2></div></div>
      <span>真实快照 · 只读查看</span>
    </header>
    <div class="aps-viewer-body"><PlanTaskAlignmentCard :block="item.block" :initial-filter="item.filter" /></div>
  </section>
</template>

<script setup>
import Icon from './Icon.vue';
import PlanTaskAlignmentCard from './PlanTaskAlignmentCard.vue';
defineProps({ item: { type: Object, required: true } });
defineEmits(['close']);
</script>
