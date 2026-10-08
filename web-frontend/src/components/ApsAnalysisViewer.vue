<template>
  <section class="aps-analysis-viewer" aria-label="APS 分析视图">
    <header>
      <div>
        <button class="icon-button" aria-label="关闭分析视图" @click="$emit('close')"><Icon name="close" /></button>
        <div><small>CHENGBEN · PLANNING VIEW</small><h2>{{ item.title }}</h2></div>
      </div>
      <span>完整矩阵 · 只读查看</span>
    </header>
    <div class="aps-viewer-body">
      <ApsAnalysisCard :block="item.block" />
    </div>
  </section>
</template>

<script setup>
import Icon from "./Icon.vue";
import ApsAnalysisCard from "./ApsAnalysisCard.vue";
defineProps({ item: { type: Object, required: true } });
defineEmits(["close"]);
</script>
