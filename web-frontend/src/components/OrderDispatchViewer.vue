<template>
  <section class="aps-analysis-viewer" aria-label="订单派工在制核对详情">
    <header>
      <div><button class="icon-button" aria-label="关闭订单派工在制核对详情" @click="$emit('close')"><Icon name="close" /></button><div><small>CHENGBEN · ORDER DISPATCH CHECK</small><h2>{{ item.title }}</h2></div></div>
      <span>受控演示 · 只读查看</span>
    </header>
    <div class="aps-viewer-body"><OrderDispatchCard :block="item.block" :initial-filter="item.filter" :initial-line="item.line" /></div>
  </section>
</template>

<script setup>
import Icon from './Icon.vue';
import OrderDispatchCard from './OrderDispatchCard.vue';
defineProps({ item: { type: Object, required: true } });
defineEmits(['close']);
</script>
