<template>
 <NativeDemoCard :block="businessBlock" :compact="compact" :focus-order-id="focusOrderId" :current-plan="currentPlan" :conversation-id="conversationId" :apply-plan="applyPlan" :restore-plan="restorePlan" :save-mrp="saveMrp" :persist-ui="persistUi" :resolve-plan="resolvePlan" :accept-result="acceptResult" @open="$emit('open',$event)" @result="$emit('result',$event)" @ask="$emit('ask',$event)" />
</template>
<script setup>
import {computed} from 'vue';
import NativeDemoCard from './NativeDemoCard.vue';
import {currentBusinessBlock} from '../services/business-presentation.js';
import {DEMAND_VERSION} from '../services/demo-plan.js';
const props=defineProps({block:{type:Object,required:true},compact:Boolean,currentPlan:Object,conversationId:String,applyPlan:Function,restorePlan:Function,saveMrp:Function,persistUi:Function,resolvePlan:Function,acceptResult:Function,focusOrderId:String});
defineEmits(['open','result','ask']);
const businessBlock=computed(()=>currentBusinessBlock(props.block,DEMAND_VERSION));
</script>
