<template>
  <article class="material-card" :class="{ compact }">
    <header class="material-card-header">
      <div>
        <small>{{ spec.eyebrow }}</small>
        <h3>{{ block.title }}</h3>
      </div>
      <div class="material-card-actions">
        <span>{{ spec.sourceLabel }}</span>
        <button type="button" @click="openViewer">查看详情 <b>↗</b></button>
      </div>
    </header>

    <div class="material-card-lead">
      <p>{{ spec.headline }}</p>
      <span>结果覆盖率 {{ model.summary.coveragePercent }}%</span>
    </div>
    <div class="material-card-snapshot">
      快照 {{ spec.snapshotId }} · 取数时间 {{ formatTime(spec.asOf) }} · 仅查询，不改排
    </div>

    <section class="material-metrics" aria-label="物料齐套关键指标">
      <div v-for="metric in spec.metrics || []" :key="metric.label" :class="`tone-${metric.tone || 'blue'}`">
        <strong>{{ metric.value }}<small>{{ metric.unit }}</small></strong>
        <span>{{ metric.label }}</span>
      </div>
    </section>

    <section class="material-preview" aria-label="订单物料齐套状态预览">
      <div class="material-section-heading">
        <div><i></i><strong>订单—物料齐套状态快览</strong></div>
        <small>点“查看详情”进入完整矩阵</small>
      </div>
      <div class="material-preview-table-wrap">
        <table>
          <thead>
            <tr>
              <th>订单 / 产品</th>
              <th v-for="column in model.columns" :key="column.materialNo">
                {{ column.label }}<small>{{ column.materialName }}</small>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in model.rows" :key="row.orderId">
              <td><strong>{{ row.orderId }}</strong><small>{{ row.productName }}</small></td>
              <td v-for="cell in row.materials" :key="cell.materialNo">
                <span class="material-status" :class="`status-${cell.status.tone}`">{{ cell.status.shortLabel }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <footer class="material-boundary">
      <strong>先说明口径</strong>
      <span>“未获得齐套标记”不是“已确认短缺”；没有返回结果的订单显示为“状态未取得”。</span>
    </footer>
  </article>
</template>

<script setup>
import { computed } from 'vue';
const props = defineProps({
  block: { type: Object, required: true },
  compact: { type: Boolean, default: false },
});
const emit = defineEmits(['open']);
const spec = computed(() => props.block.spec || {});
const model = computed(() => spec.value.model || { columns: [], rows: [], summary: { coveragePercent: 0 } });

function formatTime(value) {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('zh-CN', {
    timeZone: 'Asia/Shanghai', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hour12: false,
  }).format(date);
}

function openViewer() {
  emit('open', {
    id: `material-readiness-${spec.value.snapshotId}`,
    type: 'material-readiness',
    title: props.block.title,
    block: props.block,
  });
}

</script>
