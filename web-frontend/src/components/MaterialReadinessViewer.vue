<template>
  <section class="material-viewer" aria-label="订单物料齐套详情">
    <header class="material-viewer-header">
      <div class="material-viewer-title">
        <button class="icon-button" aria-label="关闭物料齐套详情" @click="$emit('close')"><Icon name="close" /></button>
        <div><small>CHENGBEN · MATERIAL READINESS</small><h2>{{ item.title }}</h2></div>
      </div>
      <span class="material-viewer-badge">受控演示 · 只读</span>
    </header>

    <div class="material-viewer-body">
      <section class="material-scope-strip" aria-label="快照范围">
        <div><small>APS版本</small><strong>{{ model.snapshotId }}</strong></div>
        <div><small>系统</small><strong>{{ model.systemNo }}</strong></div>
        <div><small>覆盖订单</small><strong>{{ model.summary.totalOrders }} <em>单</em></strong></div>
        <div><small>已取得齐套结果</small><strong>{{ resultCount }} <em>单 / {{ model.summary.coveragePercent }}%</em></strong></div>
        <div class="scope-note"><Icon name="info" /><span>以下内容只按快照已有字段展示，未重新计算齐套。</span></div>
      </section>

      <section class="material-viewer-intro">
        <div>
          <small>{{ spec.eyebrow }}</small>
          <h1>{{ item.title }}</h1>
          <p>{{ spec.headline }}</p>
        </div>
        <div class="material-snapshot-meta">取数时间 {{ formatTime(spec.asOf) }}<br />快照 {{ spec.snapshotId }}</div>
      </section>

      <section class="material-viewer-metrics" aria-label="齐套状态统计">
        <div v-for="metric in spec.metrics || []" :key="metric.label" :class="`tone-${metric.tone || 'blue'}`">
          <span>{{ metric.label }}</span><strong>{{ metric.value }}<small>{{ metric.unit }}</small></strong>
        </div>
      </section>

      <section class="material-legend" aria-label="状态说明">
        <span v-for="item in legend" :key="item.key"><i :class="`legend-${item.tone}`"></i>{{ item.label }}</span>
        <span class="legend-note">F 只按原系统含义展示，不改称“短缺”</span>
      </section>

      <section class="material-workspace">
        <div class="material-matrix-panel">
          <div class="material-panel-heading">
            <div><small>DETAIL MATRIX</small><h2>订单—物料齐套矩阵</h2></div>
            <span>{{ filteredRows.length }} / {{ model.rows.length }} 个代表订单</span>
          </div>
          <div class="material-toolbar">
            <div class="material-filter-chips">
              <button v-for="filter in filters" :key="filter.key" type="button" :class="{ active: activeFilter === filter.key }" @click="activeFilter = filter.key">{{ filter.label }}</button>
            </div>
            <label class="material-search"><Icon name="search" /><input v-model="query" placeholder="查订单号或产品" aria-label="查订单号或产品" /></label>
          </div>
          <div class="material-matrix-wrap">
            <table class="material-matrix">
              <thead>
                <tr>
                  <th class="material-fixed order-col">订单 / 产品</th>
                  <th v-for="column in model.columns" :key="column.materialNo" :title="`${column.materialName} · ${column.materialNo}`">
                    <span>{{ column.label }}</span><small>{{ column.materialName }}</small><em>{{ column.materialNo }}</em>
                  </th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="row in filteredRows" :key="row.orderId">
                  <td class="material-fixed order-col"><strong>{{ row.orderId }}</strong><span>{{ row.productName }}</span><small>{{ row.productNo }} · {{ formatQuantity(row.orderQuantity) }} 件</small></td>
                  <td v-for="cell in row.materials" :key="cell.materialNo" :class="{ selected: isSelected(row, cell), 'not-applicable-cell': cell.status.key === 'not-applicable' }">
                    <button type="button" class="matrix-cell" :disabled="cell.status.key === 'not-applicable'" :aria-label="`${row.orderId} ${cell.materialName} ${cell.status.label}`" @click="selectCell(row, cell)">
                      <span class="material-status" :class="`status-${cell.status.tone}`">{{ cell.status.shortLabel }}</span>
                      <small v-if="cell.evidence">{{ formatQuantity(cell.evidence.remainNum) }} 可用</small>
                      <small v-else-if="cell.status.key === 'unknown'">暂无结果</small>
                      <small v-else-if="cell.status.key === 'not-applicable'">—</small>
                    </button>
                  </td>
                </tr>
                <tr v-if="!filteredRows.length"><td class="material-empty" :colspan="model.columns.length + 1">没有符合当前筛选条件的订单</td></tr>
              </tbody>
            </table>
          </div>
          <p class="material-matrix-note">矩阵中的材料列来自当前代表订单的 BOM；未返回对应齐套结果时保留为“状态未取得”。</p>
        </div>

        <aside class="material-detail-panel" aria-label="材料明细">
          <div class="material-panel-heading detail-heading"><div><small>MATERIAL EVIDENCE</small><h2>材料明细</h2></div><span v-if="selectedCell" class="material-status" :class="`status-${selectedCell.cell.status.tone}`">{{ selectedCell.cell.status.label }}</span></div>
          <template v-if="selectedCell">
            <div class="selected-order"><strong>{{ selectedCell.row.orderId }}</strong><span>{{ selectedCell.row.productName }}</span><small>{{ selectedCell.row.productNo }}</small></div>
            <div v-if="selectedCell.row.risk?.riskLabel" class="selected-risk"><strong>{{ selectedCell.row.risk.riskLabel }}</strong><span>{{ selectedCell.row.risk.riskDetail }}</span><small>{{ selectedCell.row.risk.scenarioLabel }} · {{ selectedCell.row.risk.dataLayer || 'snapshot' }}</small></div>
            <div class="selected-material"><span>{{ selectedCell.cell.label }}</span><strong>{{ selectedCell.cell.materialName }}</strong><small>{{ selectedCell.cell.materialNo }}</small></div>
            <dl class="material-facts">
              <div><dt>本订单物料需求</dt><dd>{{ formatQuantity(selectedCell.cell.evidence?.materialNum) }}</dd></div>
              <div><dt>可用余量</dt><dd>{{ formatQuantity(selectedCell.cell.evidence?.remainNum) }}</dd></div>
              <div><dt>本订单余量</dt><dd>{{ formatQuantity(selectedCell.cell.evidence?.thisRemainNum) }}</dd></div>
              <div><dt>总余量</dt><dd>{{ formatQuantity(selectedCell.cell.evidence?.totalRemainNum) }}</dd></div>
              <div><dt>供应单号</dt><dd>{{ selectedCell.cell.evidence?.supplyOrderNo || '当前结果未带供应单' }}</dd></div>
              <div><dt>供应到货日期</dt><dd>{{ supplyDate(selectedCell.cell.evidence?.supplyDeliveryDate) }}</dd></div>
            </dl>
            <div class="material-evidence-callout" :class="`callout-${selectedCell.cell.status.tone}`">
              <strong>{{ selectedCell.cell.status.label }}</strong>
              <p v-if="selectedCell.cell.status.key === 'not-obtained'">当前快照返回了 APS 齐套结果，但该结果的标记为 F；页面不把它进一步解释为已确认短缺。</p>
              <p v-else-if="selectedCell.cell.status.key === 'ready'">当前快照返回了 APS 齐套结果，标记为 T；页面不在前端重新计算。</p>
              <p v-else>当前快照没有这笔订单—物料的齐套结果，页面不补写库存、供应或短缺结论。</p>
              <div v-if="selectedCell.cell.evidence?.riskLabel" class="material-risk-explanation"><b>{{ selectedCell.cell.evidence.riskLabel }}</b><span>{{ selectedCell.cell.evidence.riskDetail }}</span><small>证据层：{{ selectedCell.cell.evidence.dataLayer || 'snapshot' }}<template v-if="selectedCell.cell.evidence.evidenceRef"> · {{ selectedCell.cell.evidence.evidenceRef }}</template></small></div>
            </div>
            <div class="material-impact">
              <div class="material-subheading"><strong>同物料订单影响</strong><span v-if="selectedImpact">{{ selectedImpact.orderCount }} 单</span></div>
              <p v-if="selectedImpact">该物料出现在 {{ selectedImpact.orderCount }} 个订单的 BOM 中：{{ selectedImpact.orderIds.join('、') }}。</p>
              <p v-else>当前快照未提供该物料的订单级分配影响关系。</p>
            </div>
          </template>
          <div v-else class="material-empty detail-empty">选择矩阵中的一个材料单元查看明细</div>
        </aside>
      </section>

      <section class="material-boundary-panel">
        <div class="material-boundary-title"><Icon name="info" /><div><strong>本页能回答什么</strong><span>先定位订单和物料，再决定是否回原 APS 核对</span></div></div>
        <div class="material-boundary-grid">
          <p><b>能看</b>订单范围、BOM 材料、已有齐套标记、快照返回的库存/供应证据。</p>
          <p><b>不能直接看</b>前端重算后的短缺原因、准确的订单级物料分配、供应晚到对工序的确定影响。</p>
          <p><b>下一步</b>点击具体单元查看证据；若要做影响分析，需要原系统接口或补充关联字段。</p>
        </div>
      </section>
    </div>
  </section>
</template>

<script setup>
import { computed, ref, watch } from 'vue';
import Icon from './Icon.vue';
import { formatMaterialQuantity } from '../services/material-readiness.js';

const props = defineProps({ item: { type: Object, required: true } });
defineEmits(['close']);
const spec = computed(() => props.item.block?.spec || {});
const model = computed(() => spec.value.model || { columns: [], rows: [], summary: {} });
const activeFilter = ref('all');
const query = ref('');
const selectedOrderId = ref('');
const selectedMaterialNo = ref('');

const filters = [
  { key: 'all', label: '全部' },
  { key: 'ready', label: '已有齐套标记' },
  { key: 'not-obtained', label: '未获得齐套标记' },
  { key: 'unknown', label: '状态未取得' },
];
const legend = [
  { key: 'ready', label: '已有齐套标记', tone: 'ready' },
  { key: 'not-obtained', label: '未获得齐套标记', tone: 'not-obtained' },
  { key: 'unknown', label: '状态未取得', tone: 'unknown' },
  { key: 'not-applicable', label: '不适用', tone: 'not-applicable' },
];

const filteredRows = computed(() => {
  const text = query.value.trim().toLowerCase();
  return model.value.rows.filter((row) => {
    const matchesFilter = activeFilter.value === 'all' || row.status.key === activeFilter.value;
    const matchesQuery = !text || [row.orderId, row.productNo, row.productName].some((value) => String(value || '').toLowerCase().includes(text));
    return matchesFilter && matchesQuery;
  });
});

const selectedCell = computed(() => {
  const row = filteredRows.value.find((item) => item.orderId === selectedOrderId.value) || filteredRows.value[0];
  if (!row) return null;
  const cell = row.materials.find((item) => item.materialNo === selectedMaterialNo.value)
    || row.materials.find((item) => item.status.key !== 'not-applicable')
    || row.materials[0];
  return cell ? { row, cell } : null;
});
const selectedImpact = computed(() => {
  const materialNo = selectedCell.value?.cell?.materialNo;
  return model.value.impact.find((item) => item.materialNo === materialNo) || null;
});
const resultCount = computed(() => model.value.summary.readyOrders + model.value.summary.notObtainedOrders);

watch(() => model.value.rows, (rows) => {
  if (!rows.length) return;
  const first = rows.find((row) => row.status.key === 'not-obtained') || rows.find((row) => row.status.key !== 'unknown') || rows[0];
  const cell = first.materials.find((item) => item.status.key === 'not-obtained')
    || first.materials.find((item) => item.status.key === 'ready')
    || first.materials.find((item) => item.status.key !== 'not-applicable');
  selectedOrderId.value = first.orderId;
  selectedMaterialNo.value = cell?.materialNo || '';
}, { immediate: true });

function selectCell(row, cell) {
  if (cell.status.key === 'not-applicable') return;
  selectedOrderId.value = row.orderId;
  selectedMaterialNo.value = cell.materialNo;
}
function isSelected(row, cell) {
  return selectedCell.value?.row.orderId === row.orderId && selectedCell.value?.cell.materialNo === cell.materialNo;
}
function formatQuantity(value) {
  return formatMaterialQuantity(value);
}
function formatTime(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false }).format(date);
}
function supplyDate(value) {
  if (!value || String(value).startsWith('1900-01-01')) return '未提供';
  return String(value).slice(0, 10);
}
</script>

<style scoped>
.selected-risk {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-top: 10px;
  padding: 9px 10px;
  border-left: 3px solid #d89a62;
  border-radius: 6px;
  background: #fff8ef;
}

.selected-risk strong {
  color: #a96d3d;
  font-size: 10px;
}

.selected-risk span {
  color: #7e776d;
  font-size: 9px;
  line-height: 1.55;
}

.selected-risk small {
  color: #aa9b8d;
  font-size: 8px;
}

.material-risk-explanation {
  display: flex;
  flex-direction: column;
  gap: 3px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px solid #eadfd8;
}

.material-risk-explanation b {
  color: #a96d3d;
  font-size: 9px;
}

.material-risk-explanation span,
.material-risk-explanation small {
  color: #806f63;
  font-size: 8px;
  line-height: 1.55;
}
</style>
