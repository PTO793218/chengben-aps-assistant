import productionDemo from '../../../agent-backend/Data/production-demo.json' with { type: 'json' };

const STATUS = Object.freeze({
  ready: Object.freeze({ key: 'ready', label: '已有齐套标记', shortLabel: '齐套', tone: 'ready' }),
  notObtained: Object.freeze({ key: 'not-obtained', label: '未获得齐套标记', shortLabel: '未获得', tone: 'not-obtained' }),
  unknown: Object.freeze({ key: 'unknown', label: '状态未取得', shortLabel: '未取得', tone: 'unknown' }),
  notApplicable: Object.freeze({ key: 'not-applicable', label: '不适用', shortLabel: '—', tone: 'not-applicable' }),
});

const stageLabels = [
  ['磨工-', '磨工'],
  ['热处理-', '热处理'],
  ['车工-', '车工'],
  ['粗车-', '粗车'],
  ['精车-', '精车'],
];

export function materialStatus(row) {
  if (!row) return STATUS.unknown;
  if (row.fullTag === 'T' || row.status === 'full_tag_T') return STATUS.ready;
  if (row.fullTag === 'F' || row.status === 'full_tag_F') return STATUS.notObtained;
  return STATUS.unknown;
}

export function materialStatusByKey(key) {
  return Object.values(STATUS).find((item) => item.key === key) || STATUS.unknown;
}

export function formatMaterialQuantity(value) {
  if (value === null || value === undefined || value === '') return '—';
  const number = Number(value);
  if (!Number.isFinite(number)) return String(value);
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(number);
}

function shortMaterialName(name, materialNo) {
  const text = String(name || materialNo || '未命名物料');
  for (const [prefix, label] of stageLabels) {
    if (text.startsWith(prefix)) return label;
  }
  return text;
}

function orderStatus(orderId, statuses, readinessByOrder) {
  const saved = statuses.get(orderId);
  if (saved) return materialStatus(saved);
  const rows = readinessByOrder.get(orderId) || [];
  if (!rows.length) return STATUS.unknown;
  return rows.some((row) => row.fullTag === 'T') ? STATUS.ready : STATUS.notObtained;
}

function orderRisk(order, statuses) {
  const saved = statuses.get(order.orderId) || {};
  const source = saved.riskCode || saved.riskLabel || saved.riskDetail ? saved : order;
  return {
    scenarioId: source.scenarioId || order.scenarioId || '',
    scenarioLabel: source.scenarioLabel || order.scenarioLabel || '',
    riskCode: source.riskCode || order.riskCode || '',
    riskLabel: source.riskLabel || order.riskLabel || '',
    riskDetail: source.riskDetail || order.riskDetail || '',
    dataLayer: source.dataLayer || order.dataLayer || '',
    evidenceRef: Array.isArray(source.evidenceRef)
      ? source.evidenceRef.join('；')
      : (source.evidenceRef || order.evidenceRef || ''),
  };
}

function uniqueByMaterial(rows) {
  const result = new Map();
  for (const row of rows || []) {
    if (!row?.materialNo || result.has(row.materialNo)) continue;
    result.set(row.materialNo, {
      materialNo: row.materialNo,
      materialName: row.materialName || row.materialNo,
      label: shortMaterialName(row.materialName, row.materialNo),
    });
  }
  return [...result.values()];
}

function buildColumns(snapshot, selectedOrders, materialNos) {
  const bomRows = (snapshot.productBomMaterials || []).filter((row) =>
    selectedOrders.some((order) => order.productNo === row.productNo),
  );
  const evidenceRows = snapshot.readinessResults || [];
  const sourceRows = [...bomRows, ...evidenceRows];
  const preferred = materialNos?.length
    ? materialNos.map((materialNo) => sourceRows.find((row) => row.materialNo === materialNo) || { materialNo })
    : sourceRows.filter((row) => row.bomLevel !== '001');
  return uniqueByMaterial(preferred);
}

function summaryFromRows(rows) {
  const summary = { totalOrders: rows.length, readyOrders: 0, notObtainedOrders: 0, unknownOrders: 0 };
  for (const row of rows) {
    if (row.status.key === 'ready') summary.readyOrders += 1;
    else if (row.status.key === 'not-obtained') summary.notObtainedOrders += 1;
    else if (row.status.key === 'unknown') summary.unknownOrders += 1;
  }
  const obtained = summary.readyOrders + summary.notObtainedOrders;
  summary.coveragePercent = summary.totalOrders ? Number(((obtained / summary.totalOrders) * 100).toFixed(1)) : 0;
  return summary;
}

function summaryFromCoverage(snapshot, rows) {
  const coverage = snapshot.coverage || {};
  if (!Object.keys(coverage).length) return summaryFromRows(rows);
  const totalOrders = Number(coverage.orderCount ?? snapshot.orders?.length ?? 0);
  const readyOrders = Number(coverage.fullTagTRowCount ?? 0);
  const notObtainedOrders = Number(coverage.fullTagFRowCount ?? 0);
  const unknownOrders = Number(coverage.orderWithoutReadinessCount ?? Math.max(0, totalOrders - readyOrders - notObtainedOrders));
  const obtained = Number(coverage.readinessOrderCount ?? readyOrders + notObtainedOrders);
  return {
    totalOrders,
    readyOrders,
    notObtainedOrders,
    unknownOrders,
    coveragePercent: totalOrders ? Number(((obtained / totalOrders) * 100).toFixed(1)) : 0,
  };
}

export function buildMaterialReadinessModel(snapshot = {}, options = {}) {
  const allOrders = Array.isArray(snapshot.orders) ? snapshot.orders : [];
  const selectedIds = options.orderIds?.length ? new Set(options.orderIds) : null;
  const orders = selectedIds ? allOrders.filter((order) => selectedIds.has(order.orderId)) : allOrders;
  const statuses = new Map((snapshot.orderStatuses || []).map((row) => [row.orderId, row]));
  const readinessByOrder = new Map();
  const readinessByKey = new Map();
  for (const row of snapshot.readinessResults || []) {
    const rows = readinessByOrder.get(row.orderId) || [];
    rows.push(row);
    readinessByOrder.set(row.orderId, rows);
    readinessByKey.set(`${row.orderId}|${row.materialNo}`, row);
  }
  const columns = buildColumns(snapshot, orders, options.materialNos);
  const bomByProduct = new Map();
  for (const row of snapshot.productBomMaterials || []) {
    const rows = bomByProduct.get(row.productNo) || [];
    rows.push(row);
    bomByProduct.set(row.productNo, rows);
  }
  const rows = orders.map((order) => {
    const productBom = bomByProduct.get(order.productNo) || [];
    const hasProductBom = productBom.length > 0;
    const bomMaterials = new Set(productBom.filter((item) => item.bomLevel !== '001').map((item) => item.materialNo));
    return {
      ...order,
      status: orderStatus(order.orderId, statuses, readinessByOrder),
      risk: orderRisk(order, statuses),
      materials: columns.map((column) => {
        const evidence = readinessByKey.get(`${order.orderId}|${column.materialNo}`) || null;
        const applicable = hasProductBom ? bomMaterials.has(column.materialNo) : null;
        const status = applicable === false ? STATUS.notApplicable : materialStatus(evidence);
        return { ...column, applicable, status, evidence };
      }),
    };
  });
  return {
    snapshotId: snapshot.demandVer || '—',
    systemNo: snapshot.systemNo || '—',
    asOf: snapshot.asOf || '',
    source: snapshot.source || 'business-db-readonly-snapshot',
    columns,
    rows,
    summary: options.summaryFromCoverage ? summaryFromCoverage(snapshot, rows) : summaryFromRows(rows),
    impact: snapshot.materialImpactSummary || [],
    limitations: snapshot.limitations || [],
  };
}

export const demoMaterialSnapshot = productionDemo.materialReadiness;

export function buildMaterialReadinessBlock(snapshot = demoMaterialSnapshot) {
  const model = buildMaterialReadinessModel(snapshot, {
    orderIds: snapshot.matrixOrderIds,
    materialNos: snapshot.matrixMaterialNos,
    summaryFromCoverage: true,
  });
  const summary = model.summary;
  return {
    type: 'material-readiness',
    title: '订单—物料齐套分析',
    spec: {
      eyebrow: 'APS MATERIAL READINESS',
      sourceLabel: '业务库材料快照 · 只读',
      snapshotId: `${model.snapshotId} · 系统${model.systemNo}`,
      asOf: model.asOf,
      headline: `当前版本共${summary.totalOrders}个订单，${summary.readyOrders}个已有齐套标记，${summary.notObtainedOrders}个未获得齐套标记，${summary.unknownOrders}个状态未取得。`,
      metrics: [
        { label: '订单总数', value: summary.totalOrders, unit: '单', tone: 'blue' },
        { label: '已有齐套标记', value: summary.readyOrders, unit: '单', tone: 'ready' },
        { label: '未获得齐套标记', value: summary.notObtainedOrders, unit: '单', tone: 'not-obtained' },
        { label: '状态未取得', value: summary.unknownOrders, unit: '单', tone: 'unknown' },
      ],
      model,
      limitations: model.limitations,
    },
  };
}
