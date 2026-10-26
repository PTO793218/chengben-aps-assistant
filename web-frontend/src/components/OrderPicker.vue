<template>
 <div class="order-picker"><label><span>{{ label }}</span><input v-model="query" type="search" :aria-label="'搜索'+label" placeholder="搜索订单号或产品" :disabled="disabled" /></label>
 <select :value="modelValue" :aria-label="label" :disabled="disabled" @change="$emit('update:modelValue',$event.target.value)"><option value="">{{ allowAll?'整批订单':'请选择订单' }}</option><option v-for="order in options" :key="order.orderId" :value="order.orderId">{{ order.orderId }} · {{ order.product||'需求 '+number(order.quantity) }}{{ order.taskTag==='T'?' · 派工在制':'' }}</option></select>
 <small v-if="query&&!matches.length">未找到匹配订单，请调整搜索内容。</small><small v-else-if="matches.length>30">显示前30条，请继续输入缩小范围。</small></div>
</template>
<script setup>
import {computed,ref} from 'vue';import {number} from '../services/business-presentation.js';
const props=defineProps({orders:{type:Array,default:()=>[]},modelValue:{type:String,default:''},label:{type:String,default:'目标订单'},disabled:Boolean,allowAll:Boolean});defineEmits(['update:modelValue']);const query=ref('');
const matches=computed(()=>props.orders.filter(o=>(o.orderId+' '+(o.product||'')).toLowerCase().includes(query.value.trim().toLowerCase())));
const options=computed(()=>{const shown=matches.value.slice(0,30),selected=props.orders.find(o=>o.orderId===props.modelValue);return selected&&!shown.some(o=>o.orderId===selected.orderId)?[selected,...shown]:shown;});
</script>
<style scoped>
.order-picker{display:grid;gap:8px}.order-picker label{display:grid;gap:8px;font-size:13px}.order-picker input,.order-picker select{width:100%;min-height:40px;padding:9px 10px;border:1px solid #cbd8e8;border-radius:7px;background:white;color:#314761;font-size:13px}.order-picker small{color:#647991;font-size:12px}.order-picker input:focus-visible,.order-picker select:focus-visible{outline:2px solid #2c7bea;outline-offset:2px}
</style>
