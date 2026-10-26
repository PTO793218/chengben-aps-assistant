<template>
 <section class="business-table" :class="{preview:compact}" :aria-label="label">
  <div v-if="!compact" class="table-tools">
   <label class="table-search"><span>搜索</span><input v-model="query" type="search" :aria-label="'搜索'+label" :placeholder="searchPlaceholder" /></label>
   <label v-for="filter in filters" :key="filter.key"><span>{{ filter.label }}</span><select v-model="chosen[filter.key]" :aria-label="filter.label"><option value="">全部</option><option v-for="option in filter.options" :key="option.value" :value="option.value">{{ option.label }}</option></select></label>
  </div>
  <div class="table-viewport"><table><thead><tr><th v-for="column in columns" :key="column.key" scope="col">{{ column.label }}</th></tr></thead><tbody>
   <tr v-for="row in result.rows" :key="keyOf(row)" :class="{selected:row.orderId&&row.orderId===selectedId}">
    <td v-for="(column,index) in columns" :key="column.key"><button v-if="selectable&&row.orderId&&index===0" class="row-select" type="button" :aria-label="'查看订单 '+row.orderId" @click="$emit('select',row)">{{ cell(column,row) }}</button><template v-else>{{ cell(column,row) }}</template><small v-if="column.secondary">{{ column.secondary(row) }}</small></td>
   </tr>
  </tbody></table><p v-if="!result.rows.length" class="table-empty" role="status">{{ query||Object.values(chosen).some(Boolean)?'未找到匹配记录，请调整搜索或筛选条件。':emptyText }}</p></div>
  <footer class="table-pagination"><span v-if="compact">显示 {{ result.rows.length }} 条，共 {{ rows.length }} 条</span><span v-else>第 {{ result.start }}—{{ result.end }} 条，共 {{ result.filteredTotal }} 条<template v-if="result.filteredTotal!==result.total">（全部 {{ result.total }} 条）</template></span>
   <nav v-if="!compact&&result.pages>1" :aria-label="label+'分页'"><button type="button" :disabled="result.page<=1" :aria-label="label+'上一页'" @click="page=result.page-1">上一页</button><span>{{ result.page }} / {{ result.pages }}</span><button type="button" :disabled="result.page>=result.pages" :aria-label="label+'下一页'" @click="page=result.page+1">下一页</button></nav>
  </footer>
 </section>
</template>
<script setup>
import {computed,reactive,ref,watch} from 'vue';
import {selectPage,PREVIEW_SIZE,DETAIL_PAGE_SIZE} from '../services/business-presentation.js';
const props=defineProps({rows:{type:Array,default:()=>[]},columns:{type:Array,default:()=>[]},compact:Boolean,label:{type:String,default:'订单明细'},rowKey:{type:[String,Function],default:'orderId'},selectedId:String,selectable:Boolean,initialQuery:{type:String,default:''},filters:{type:Array,default:()=>[]},searchPlaceholder:{type:String,default:'输入订单号、产品或相关信息'},emptyText:{type:String,default:'当前范围没有可显示的记录。'}});
const emit=defineEmits(['select','page-change']);const query=ref(props.initialQuery),page=ref(1),chosen=reactive(Object.fromEntries(props.filters.map(f=>[f.key,''])));
const cell=(column,row)=>column.text?column.text(row):String(row[column.key]??'—');
const searchableRows=computed(()=>props.rows.map(row=>({...row,__search:props.columns.map(c=>cell(c,row)+' '+(c.secondary?.(row)||'')).join(' ')})));
const result=computed(()=>selectPage(searchableRows.value,{query:props.compact?'':query.value,page:props.compact?1:page.value,pageSize:props.compact?PREVIEW_SIZE:DETAIL_PAGE_SIZE,filters:props.compact?{}:chosen,keys:['__search']}));
const keyOf=row=>typeof props.rowKey==='function'?props.rowKey(row):row[props.rowKey];
watch([query,()=>JSON.stringify(chosen),()=>props.rows],()=>{page.value=1;});
watch(()=>props.initialQuery,value=>{query.value=value||'';});
watch(result,value=>emit('page-change',value.rows),{immediate:true});
</script>
<style scoped>
.business-table{margin:16px 0;color:#314761}.table-tools{display:flex;gap:14px;align-items:end;flex-wrap:wrap;margin-bottom:12px}.table-tools label{display:flex;gap:6px;flex-direction:column;font-size:13px}.table-search{flex:1;min-width:200px}.table-tools input,.table-tools select{min-height:38px;padding:8px 10px;border:1px solid #cdd9e8;border-radius:7px;color:inherit;background:white}.table-viewport{overflow-x:auto;border:1px solid #e0e8f1;border-radius:10px}.table-viewport table{width:100%;border-collapse:collapse;font-size:13px}.table-viewport th,.table-viewport td{padding:12px;text-align:left;border-bottom:1px solid #e7edf4;vertical-align:top}.table-viewport th{background:#f3f7fc;color:#566c85;font-weight:600;white-space:nowrap}.table-viewport tr:last-child td{border-bottom:0}.table-viewport td{line-height:1.6;overflow-wrap:anywhere}.table-viewport td small{display:block;margin-top:4px;color:#647991;font-size:12px}.row-select{border:0;padding:0;background:none;color:#2766b5;font-size:inherit;cursor:pointer;text-align:left}.row-select:hover{text-decoration:underline}.selected td{background:#edf5ff}.table-pagination{display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:10px;margin-top:10px;font-size:12px;color:#647991}.table-pagination nav{display:flex;align-items:center;gap:12px}.table-pagination button{padding:6px 10px;border:1px solid #cdd9e8;border-radius:6px;background:white;color:#315b8c;cursor:pointer}.table-pagination button:disabled{opacity:.45;cursor:default}.table-empty{padding:22px;margin:0;color:#647991;font-size:13px}.preview .table-viewport{overflow:visible}.preview th,.preview td{padding:10px;font-size:13px}.row-select:focus-visible,.table-pagination button:focus-visible,input:focus-visible,select:focus-visible{outline:2px solid #2c7bea;outline-offset:2px}
</style>
