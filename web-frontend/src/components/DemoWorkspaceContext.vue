<template>
<section v-if="workspace?.native" class="business-related" aria-label="继续查看">
 <span>继续查看</span><button v-for="action in relatedActions(workspace)" :key="action.label" type="button" @click="$emit('ask',action.question)">{{ action.label }}</button>
</section>

</template>
<script setup>
import {relatedActions} from '../services/business-presentation.js';
defineProps({workspace:Object});defineEmits(['ask']);

</script>
<style scoped>
.business-related{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin:12px 0 20px;font-size:13px;color:#647991}.business-related button{padding:7px 12px;border:1px solid #d5e0ed;background:white;border-radius:7px;color:#315c8f;cursor:pointer}.business-related button:focus-visible{outline:2px solid #2c7bea;outline-offset:2px}

.demo-workspace-context{margin:14px 0 22px;padding:15px 18px;border:1px solid #dce6f1;border-radius:12px;background:#f5f8fc;color:#314761;font-size:12px;line-height:1.8}.demo-workspace-context header{display:flex;gap:12px;flex-wrap:wrap}.demo-workspace-context header span,.demo-workspace-context small{color:#6a7c90}.workspace-order{padding:9px 0;border-bottom:1px solid #e2e9f2}.workspace-order:last-of-type{border:0}.workspace-order p{margin:4px 0}.workspace-order nav,.workspace-release{display:flex;gap:8px;flex-wrap:wrap;margin:9px 0}.demo-workspace-context button{padding:5px 11px;border:1px solid #cbd8e8;border-radius:7px;background:white;color:#315c8f;cursor:pointer}
</style>
