import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { buildMaterialReadinessBlock, buildMaterialReadinessModel, demoMaterialSnapshot } from '../../web-frontend/src/services/material-readiness.js';

const projectData = JSON.parse(fs.readFileSync(new URL('../../agent-backend/Data/production-demo.json', import.meta.url)));
const snapshot = projectData.materialReadiness;
const scenarioIds = [
  'S01-all-ready',
  'S02-key-shortage',
  'S03-stock-occupied',
  'S04-supply-late',
  'S05-quality-blocked',
  'S06-readiness-unknown',
  'S07-shared-material',
];

test('material readiness snapshot carries seven risk stories across thirteen linked orders', () => {
  assert.equal(snapshot.source, 'synthetic-demo');
  assert.equal(snapshot.orders.length, 13);
  assert.deepEqual(snapshot.orders.filter((row) => !row.scenarioId.startsWith('S08-')).map((row) => row.scenarioId).sort(), scenarioIds.sort());
  assert.ok(snapshot.dataClassification.databaseOriginal);
  assert.ok(snapshot.dataClassification.derived);
  assert.ok(snapshot.dataClassification.mock);
  assert.ok(snapshot.orders.every((row) => row.dataLayer === 'mock'));
  assert.ok(snapshot.productBomMaterials.every((row) => row.dataLayer === 'mock'));
});

test('order statuses and readiness rows reconcile without inventing unknown results', () => {
  const orderIds = new Set(snapshot.orders.map((row) => row.orderId));
  const statuses = new Map(snapshot.orderStatuses.map((row) => [row.orderId, row]));
  const resultsByOrder = new Map();
  for (const row of snapshot.readinessResults) {
    assert.ok(orderIds.has(row.orderId));
    resultsByOrder.set(row.orderId, [...(resultsByOrder.get(row.orderId) || []), row]);
  }

  assert.equal(statuses.size, snapshot.orders.length);
  assert.equal(snapshot.orderStatuses.filter((row) => row.status === 'full_tag_T').length, 8);
  assert.equal(snapshot.orderStatuses.filter((row) => row.status === 'full_tag_F').length, 4);
  assert.equal(snapshot.orderStatuses.filter((row) => row.status === 'status_not_obtained').length, 1);

  for (const status of snapshot.orderStatuses) {
    const rows = resultsByOrder.get(status.orderId) || [];
    assert.equal(status.readinessRowCount, rows.length);
    assert.equal(status.fullTagTRowCount, rows.filter((row) => row.fullTag === 'T').length);
    if (status.status === 'status_not_obtained') assert.equal(rows.length, 0);
  }
});

test('frontend card summary reconciles with order statuses when coverage is absent', () => {
  const summary = buildMaterialReadinessBlock(snapshot).spec.model.summary;
  assert.deepEqual(summary, {
    totalOrders: 13,
    readyOrders: 8,
    notObtainedOrders: 4,
    unknownOrders: 1,
    coveragePercent: 92.3,
  });
});

test('frontend fallback reads the same snapshot as the embedded backend data', () => {
  assert.equal(demoMaterialSnapshot.snapshotId, snapshot.snapshotId);
  assert.deepEqual(demoMaterialSnapshot.orders, snapshot.orders);
  assert.deepEqual(demoMaterialSnapshot.orderStatuses, snapshot.orderStatuses);
  assert.deepEqual(demoMaterialSnapshot.readinessResults, snapshot.readinessResults);
});

test('frontend rows expose scenario risk and evidence provenance for the detail view', () => {
  const model = buildMaterialReadinessModel(snapshot);
  const occupied = model.rows.find((row) => row.orderId === 'Z9900006');
  const lateSupply = model.rows.find((row) => row.orderId === 'Z9900007');
  assert.equal(occupied.risk.riskCode, 'stock-occupied');
  assert.match(occupied.risk.riskLabel, /可分配量不足/);
  assert.match(occupied.risk.riskDetail, /受控 mock/);
  const evidence = lateSupply.materials.find((cell) => cell.evidence?.riskCode === 'supply-late')?.evidence;
  assert.equal(evidence.riskCode, 'supply-late');
  assert.equal(evidence.dataLayer, 'mock');
  assert.equal(evidence.arrivalFeasible, false);
});
