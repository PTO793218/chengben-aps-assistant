<template><section class="aps-analysis-viewer" aria-label="操作详情"><header><div><button class="icon-button" aria-label="关闭操作详情" @click="$emit('close')"><Icon name="close"/></button><h2>{{ item.title }}</h2></div></header><div class="aps-viewer-body"><InteractiveDemoCard :block="item.block" :focus-order-id="item.focusOrderId" :current-plan="currentPlan" :conversation-id="conversationId" :apply-plan="applyPlan" :restore-plan="restorePlan" :save-mrp="saveMrp" :persist-ui="persistUi" :resolve-plan="resolvePlan" :accept-result="acceptResult" @result="$emit('result',$event)" @ask="$emit('ask',$event)"/></div></section></template>
<script setup>
import Icon from './Icon.vue';import InteractiveDemoCard from './InteractiveDemoCard.vue';
defineProps({item:Object,currentPlan:Object,conversationId:String,applyPlan:Function,restorePlan:Function,saveMrp:Function,persistUi:Function,resolvePlan:Function,acceptResult:Function});defineEmits(['close','result','ask']);
</script>
