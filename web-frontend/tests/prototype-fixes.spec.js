import { test, expect } from '@playwright/test';

const response = (text, blocks = []) => ({
  contentType: 'text/event-stream',
  body: `event: delta\ndata: ${JSON.stringify({ text })}\n\nevent: ui_payload\ndata: ${JSON.stringify({ blocks })}\n\nevent: done\ndata: {"toolCount":0}\n\n`,
});
const task = (number, orderNo, line) => ({
  rowType: 'task', id: number, number, orderNo, line, product: '测试产品',
  statusKey: 'run', taskStatus: '运行中', status: '运行中',
  dispatchTaskCount: 1, inProgressTaskCount: 1, reason: `关联订单 ${orderNo}`,
});
const tasks = [task('T1', 'O1', 'L1'), task('T2', 'O2', 'L2')];
const dispatch = {
  type: 'order-dispatch', title: '订单—派工在制核对',
  spec: {
    sourceLabel: '受控演示 · 非实时', snapshotId: 'TEST-DISPATCH', asOf: '2026-09-26T17:30:00+08:00',
    headline: '两条产线各一条运行中任务。', metrics: [],
    orderFlow: { available: false }, detailGroups: { allOrders: [], allTasks: tasks, runTasks: tasks },
  },
};

test.beforeEach(async ({ page }) => {
  await page.route('**/api/agent/health', route => route.fulfill({ json: { status: 'ok', mode: 'model' } }));
  await page.route('**/api/speech-health', route => route.fulfill({ json: { status: 'ok', configured: false } }));
});

async function ask(page, question = '查询演示派工') {
  await page.getByRole('textbox', { name: '输入问题' }).fill(question);
  await page.getByRole('button', { name: '发送消息', exact: true }).click();
}
async function openDispatch(page) {
  await page.route('**/api/agent/chat/stream', route => route.fulfill(response('原回答', [dispatch])));
  await page.goto('/');
  await ask(page);
  await page.getByRole('button', { name: /查看详细/ }).click();
}

test('dispatch task table exposes task number separately from order and line', async ({ page }) => {
  await openDispatch(page);
  const canvas = page.locator('.alignment-charts .alignment-chart-panel').nth(1).locator('canvas');
  await expect(canvas).toBeVisible();
  const box = await canvas.boundingBox();
  await canvas.click({ position: { x: 42 + (box.width - 56) / 4, y: 38 + (box.height - 72) / 2 } });
  const table = page.locator('.alignment-detail table');
  await expect(table.locator('thead')).toContainText('任务号');
  await expect(table).toContainText('T1');
  await expect(table).toContainText('O1');
  await expect(table).toContainText('L1');
});

test('dispatch chart click filters the selected line and status without querying again', async ({ page }) => {
  let requestCount = 0;
  await page.route('**/api/agent/chat/stream', route => {
    requestCount += 1;
    return route.fulfill(response('原回答', [dispatch]));
  });
  await page.goto('/');
  await ask(page);
  await page.getByRole('button', { name: /查看详细/ }).click();
  const canvas = page.locator('.alignment-charts .alignment-chart-panel').nth(1).locator('canvas');
  await expect(canvas).toBeVisible();
  const box = await canvas.boundingBox();
  await canvas.click({ position: { x: 42 + (box.width - 56) / 4, y: 38 + (box.height - 72) / 2 } });
  await expect(page.locator('.alignment-detail tbody tr')).toHaveCount(1);
  await expect(page.locator('.alignment-detail')).toContainText('L1');
  await expect(page.locator('.alignment-detail')).toContainText('运行中任务');
  await page.getByRole('button', { name: '清除产线筛选' }).click();
  await expect(page.locator('.alignment-detail tbody tr')).toHaveCount(2);
  expect(requestCount).toBe(1);
});

test('failed health check reports failure and can reconnect', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/agent/health', route => {
    requests += 1;
    return requests === 1 ? route.fulfill({ status: 503, body: 'unavailable' })
      : route.fulfill({ json: { status: 'ok', mode: 'model' } });
  });
  await page.goto('/');
  await expect(page.locator('.connection-state')).toContainText('连接失败');
  await page.getByRole('button', { name: '重新连接', exact: true }).click();
  await expect(page.locator('.connection-state')).toContainText('已连接模型');
  expect(requests).toBe(2);
});

test('failed retry preserves the original answer, card, question and input draft after reload', async ({ page }) => {
  const requests = [];
  await page.route('**/api/agent/chat/stream', route => {
    requests.push(route.request().postDataJSON());
    return requests.length === 1 ? route.fulfill(response('原回答', [dispatch]))
      : route.fulfill({ status: 503, body: 'unavailable' });
  });
  await page.goto('/');
  await ask(page);
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await page.getByRole('textbox', { name: '输入问题' }).fill('未发送的下一问');
  await page.getByRole('button', { name: '重新生成', exact: true }).click();
  await expect(page.locator('.message-error')).toContainText('503');
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await expect(page.locator('.order-dispatch-card')).toHaveCount(1);
  await expect(page.locator('.message.user')).toHaveCount(1);
  await expect(page.getByRole('textbox', { name: '输入问题' })).toHaveValue('未发送的下一问');
  expect(requests[1].history).toEqual([]);
  await page.reload();
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await expect(page.locator('.order-dispatch-card')).toHaveCount(1);
  await expect(page.locator('.message.user')).toHaveCount(1);
});

test('successful retry keeps earlier evidence and passes only the latest answer to the next question', async ({ page }) => {
  const requests = [];
  await page.route('**/api/agent/chat/stream', route => {
    requests.push(route.request().postDataJSON());
    return route.fulfill(response(requests.length === 1 ? '原回答' : requests.length === 2 ? '新回答' : '后续回答', requests.length === 1 ? [dispatch] : []));
  });
  await page.goto('/');
  await ask(page);
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '重新生成', exact: true }).click();
  await expect(page.getByText('新回答', { exact: true })).toBeVisible();
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await expect(page.locator('.order-dispatch-card')).toHaveCount(1);
  await expect(page.locator('.message.user')).toHaveCount(1);
  expect(requests[1].history).toEqual([]);
  await ask(page, '继续核对');
  await expect(page.getByText('后续回答', { exact: true })).toBeVisible();
  expect(requests[2].history).toEqual([
    { role: 'user', content: '查询演示派工' }, { role: 'assistant', content: '新回答' },
  ]);
  await page.reload();
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await expect(page.getByText('新回答', { exact: true })).toBeVisible();
});

test('stopping retry leaves original evidence accessible', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/agent/chat/stream', async route => {
    requests += 1;
    if (requests === 1) return route.fulfill(response('原回答', [dispatch]));
    await new Promise(resolve => setTimeout(resolve, 1200));
    await route.abort().catch(() => {});
  });
  await page.goto('/');
  await ask(page);
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '重新生成', exact: true }).click();
  await expect(page.getByText('原回答', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: '停止生成', exact: true }).click();
  await expect(page.getByText('已停止生成', { exact: true })).toBeVisible();
  await expect(page.locator('.order-dispatch-card')).toHaveCount(1);
  await expect(page.locator('.message.user')).toHaveCount(1);
});
