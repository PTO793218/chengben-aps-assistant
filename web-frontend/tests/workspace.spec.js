import { test, expect } from "@playwright/test";
import fs from "node:fs";
test("homepage order suggestion uses the controlled demo order", async ({ page }) => {
  const requests = [];
  await page.route("**/api/agent/chat/stream", route => {
    requests.push(route.request().postDataJSON());
    return route.fulfill({
      contentType: "text/event-stream",
      body: 'event: delta\ndata: {"text":"已读取演示数据"}\n\nevent: done\ndata: {"toolCount":1}\n\n',
    });
  });
  await page.goto("/");
  await page.getByRole("button", { name: /演示订单 Z9900001 怎么安排/ }).click();
  await expect(page.getByText("已读取演示数据", { exact: true })).toBeVisible();
  expect(requests).toHaveLength(1);
  expect(requests[0].message).toBe("查询演示订单 Z9900001 的排程");
});
test("homepage and about text state the supported data scope", async ({ page }) => {
  const requests = [];
  await page.route("**/api/agent/chat/stream", route => {
    requests.push(route.request().postDataJSON());
    return route.fulfill({
      contentType: "text/event-stream",
      body: 'event: delta\ndata: {"text":"已读取演示快照"}\n\nevent: done\ndata: {"toolCount":1}\n\n',
    });
  });
  await page.goto("/");
  await expect(page.getByRole("button", { name: /哪些订单有计划交期风险/ })).toBeVisible();
  await expect(page.getByRole("button", { name: /演示订单 Z9900001 怎么安排/ })).toBeVisible();
  const material = page.getByRole("button", { name: /哪些订单的物料齐套状态需核对/ });
  await expect(material).toBeVisible();
  await page.getByRole("button", { name: "打开用户菜单" }).click();
  await page.getByRole("button", { name: "关于诚本 APS 助手" }).click();
  await expect(page.locator(".about-modal")).toContainText("首页入口均使用内置演示数据");
  await expect(page.locator(".about-modal")).toContainText("明确查询真实单订单时，可读取已配置的业务库只读快照");
  await page.getByRole("button", { name: "知道了" }).click();
  await material.click();
  await expect.poll(() => requests.length).toBe(1);
  expect(requests[0].message).toBe("演示快照中哪些订单的物料齐套状态需要核对？");
});
test("regeneration preserves one question when audio playback fails", async ({ page }) => {
  const requests = [];
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.addInitScript(() => {
    window.AudioContext = class {
      constructor() { throw new Error("测试：音频不可用"); }
    };
  });
  await page.route("**/api/speech-health", (route) => route.fulfill({
    json: { status: "ok", configured: true },
  }));
  await page.route("**/api/agent/chat/stream", (route) => {
    requests.push(route.request().postDataJSON());
    return route.fulfill({
      contentType: "text/event-stream",
      body: 'event: delta\ndata: {"text":"文字回答正常完成"}\n\nevent: done\ndata: {"toolCount":0}\n\n',
    });
  });
  await page.goto("/");
  await page.getByRole("button", { name: "打开用户菜单", exact: true }).click();
  await page.getByRole("button", { name: "设置 偏好与语音" }).click();
  await page.getByRole("checkbox", { name: "自动朗读回答" }).check();
  await page.getByRole("button", { name: "完成", exact: true }).click();
  await page.getByRole("textbox", { name: "输入问题" }).fill("验证重新生成");
  await page.getByRole("button", { name: "发送消息", exact: true }).click();
  await expect(page.getByText("文字回答正常完成", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "重新生成", exact: true }).click();
  await expect(page.getByText("文字回答正常完成", { exact: true })).toHaveCount(2);
  await expect(page.locator(".message.user")).toHaveCount(1);
  expect(requests).toHaveLength(2);
  expect(requests[1].message).toBe("验证重新生成");
  expect(requests[1].history).toEqual([]);
  await page.reload();
  await expect(page.locator(".message.user")).toHaveCount(1);
  await expect(page.getByText("文字回答正常完成", { exact: true })).toHaveCount(2);
  expect(errors).toEqual([]);
});

test("settings, sidebar and image workspace", async ({
  page,
}) => {
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "让想法，从对话开始。" }),
  ).toBeVisible();
  fs.mkdirSync("../temp/screenshots", { recursive: true });
  await page.screenshot({
    path: "../temp/screenshots/chat.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "打开用户菜单", exact: true }).click();
  await page.getByRole("button", { name: "设置 偏好与语音" }).click();
  await expect(
    page.getByRole("dialog", { name: "设置", exact: true }),
  ).toBeVisible();
  await page.getByRole("checkbox", { name: "紧凑布局" }).check();
  await page.screenshot({
    path: "../temp/screenshots/settings.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "完成", exact: true }).click();
  await page.reload();
  await page.getByRole("button", { name: "打开用户菜单", exact: true }).click();
  await page.getByRole("button", { name: "设置 偏好与语音" }).click();
  await expect(page.getByRole("checkbox", { name: "紧凑布局" })).toBeChecked();
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "收起侧栏", exact: true }).click();
  await expect(page.locator(".app-shell")).toHaveClass(/collapsed/);
  await page.getByRole("button", { name: "展开侧栏", exact: true }).click();
  await page.getByRole("button", { name: /展开图片，专注细节/ }).click();
  await expect(page.getByRole("region", { name: "内容查看区" })).toBeVisible();
  await expect(page.locator(".asset-rail")).toBeVisible();
  await page.getByRole("button", { name: "放大图片" }).click();
  await expect(page.getByRole("button", { name: "125%" })).toBeVisible();
  await page.screenshot({
    path: "../temp/screenshots/image.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "关闭查看区" }).click();
  expect(errors).toEqual([]);
});

test("welcome shortcuts include the order dispatch check", async ({ page }) => {
  const requests = [];
  await page.route("**/api/agent/chat/stream", (route) => {
    requests.push(route.request().postDataJSON());
    return route.fulfill({
      contentType: "text/event-stream",
      body: 'event: delta\ndata: {"text":"已完成计划与任务核对"}\n\nevent: done\ndata: {"toolCount":1}\n\n',
    });
  });
  await page.goto("/");
  await expect(page.locator(".suggestion-grid > button")).toHaveCount(6);
  await page.getByRole("button", { name: /哪些订单已经形成派工/ }).click();
  await expect(page.locator(".message.user")).toContainText(
    "哪些订单已经形成派工？请重点看派工在制和暂未取得来源订单的任务。",
  );
  await expect.poll(() => requests.length).toBe(1);
  expect(requests[0].message).toBe(
    "哪些订单已经形成派工？请重点看派工在制和暂未取得来源订单的任务。",
  );
});

test("SSE chat rendering, history restoration and cancellation", async ({
  page,
}) => {
  // Browser test uses deterministic SSE. Real provider is exercised separately by agent-evals.
  await page.route("**/api/agent/chat/stream", (route) =>
    route.fulfill({
      contentType: "text/event-stream",
      body: 'event: start\ndata: {}\n\nevent: delta\ndata: {"text":"这是示例回答，合计 320。"}\n\nevent: ui_payload\ndata: {"blocks":[{"type":"chart","title":"测试图表","spec":{"chartType":"bar","xField":"name","yField":"value","data":[{"name":"A","value":320}]}}]}\n\nevent: done\ndata: {"toolCount":1}\n\n',
    }),
  );
  await page.goto("/");
  await page.getByRole("textbox", { name: "输入问题" }).fill("查询示例");
  await page.getByRole("button", { name: "发送消息", exact: true }).click();
  await expect(page.getByText("这是示例回答，合计 320。")).toBeVisible();
  await expect(page.locator(".chart-canvas canvas")).toBeVisible();
  await page.reload();
  await expect(page.getByText("这是示例回答，合计 320。")).toBeVisible();
  await page.getByRole("button", { name: "新建对话", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "让想法，从对话开始。" }),
  ).toBeVisible();
  await page.route("**/api/agent/chat/stream", async (route) => {
    await new Promise((r) => setTimeout(r, 2500));
    await route.abort().catch(() => {});
  });
  await page.getByRole("textbox", { name: "输入问题" }).fill("测试停止");
  await page.getByRole("button", { name: "发送消息", exact: true }).click();
  await page.getByRole("button", { name: "停止生成", exact: true }).click();
  await expect(page.getByText("已停止生成", { exact: true })).toBeVisible();
});

test("order dispatch check opens the order and task detail view", async ({ page }) => {
  await page.setViewportSize({ width: 1815, height: 1028 });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  page.on("response", response => { if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`); });
  await page.route("**/api/agent/health", route => route.fulfill({ json: { status: "ok", mode: "demo" } }));
  await page.route("**/api/speech-health", route => route.fulfill({ json: { status: "ok", configured: true } }));
  const block = {
    type: "order-dispatch",
    title: "订单—派工在制核对",
    spec: {
      eyebrow: "APS READ-ONLY ORDER DISPATCH CHECK",
      sourceLabel: "受控订单—派工演示数据 · 非实时",
      snapshotId: "CB-ORDER-DISPATCH-DEMO-20260921",
      asOf: "2026-09-21T17:30:00+08:00",
      headline: "当前快照包含 2 个订单，其中 1 个存在派工在制任务；1 个订单尚未找到当前任务，另有 1 条当前任务暂未取得来源订单。",
      metrics: [
        { label: "订单总数", value: "2", unit: "个", filter: "all-orders", available: true },
        { label: "派工在制", value: "1", unit: "个", filter: "dispatch-in-progress", available: true },
        { label: "已完成", value: "0", unit: "个", filter: "dispatch-completed", available: true },
        { label: "已关闭", value: "0", unit: "个", filter: "dispatch-closed", available: true },
        { label: "未形成当前任务", value: "1", unit: "个", filter: "task-not-generated", available: true },
        { label: "来源未取得任务", value: "1", unit: "条", filter: "unmatched-task", available: true },
      ],
      orderFlow: { available: true, total: 2, dispatchInProgress: 1, completed: 0, closed: 0, taskNotGenerated: 1, unknown: 0 },
      status: [
        { label: "运行中", value: 1, filter: "run", tone: "blue" },
        { label: "已完成", value: 0, filter: "finish", tone: "green" },
        { label: "已关闭", value: 0, filter: "close", tone: "violet" },
        { label: "来源未取得", value: 1, filter: "unmatched-task", tone: "amber" },
      ],
      detailGroups: {
        allOrders: [
          { rowType: "order", id: "O1", orderNo: "O1", product: "产品A", line: "L1", dispatchTaskCount: 1, inProgressTaskCount: 1, taskStatus: "运行中", status: "派工在制", statusKey: "dispatch_in_progress", taskTag: "T", releaseStatus: "下发/流转状态未取得", risk: "normal", reason: "存在运行中任务" },
          { rowType: "order", id: "O2", orderNo: "O2", product: "产品B", line: "L2", dispatchTaskCount: 0, inProgressTaskCount: 0, taskStatus: "未形成当前任务", status: "未形成当前任务", statusKey: "task_not_generated", taskTag: "—", releaseStatus: "下发/流转状态未取得", risk: "attention", reason: "原因尚未查明" },
        ],
        dispatchInProgress: [{ rowType: "order", id: "O1", orderNo: "O1", product: "产品A", line: "L1", dispatchTaskCount: 1, inProgressTaskCount: 1, taskStatus: "运行中", status: "派工在制", statusKey: "dispatch_in_progress", taskTag: "T", releaseStatus: "下发/流转状态未取得", risk: "normal", reason: "存在运行中任务" }],
        dispatchCompleted: [], dispatchClosed: [], taskNotGenerated: [{ rowType: "order", id: "O2", orderNo: "O2", product: "产品B", line: "L2", dispatchTaskCount: 0, inProgressTaskCount: 0, taskStatus: "未形成当前任务", status: "未形成当前任务", statusKey: "task_not_generated", taskTag: "—", releaseStatus: "下发/流转状态未取得", risk: "attention", reason: "原因尚未查明" }], unknown: [],
        allTasks: [
          { rowType: "task", id: "T1", number: "T1", orderNo: "O1", product: "产品A", line: "L1", dispatchTaskCount: 1, inProgressTaskCount: 1, taskStatus: "运行中", status: "运行中", statusKey: "run", taskTag: "T", releaseStatus: "下发/流转状态未取得", risk: "normal", reason: "关联订单 O1" },
          { rowType: "task", id: "T2", number: "T2", orderNo: "", product: "未知产品", line: "L1", dispatchTaskCount: 1, inProgressTaskCount: 1, taskStatus: "运行中", status: "运行中", statusKey: "unmatched-task", taskTag: "T", releaseStatus: "下发/流转状态未取得", risk: "attention", reason: "来源订单未取得" },
        ],
        runTasks: [{ rowType: "task", id: "T1", number: "T1", orderNo: "O1", product: "产品A", line: "L1", statusKey: "run", status: "运行中" }],
        finishTasks: [], closeTasks: [], unmatchedTask: [{ rowType: "task", id: "T2", number: "T2", orderNo: "", product: "未知产品", line: "L1", statusKey: "unmatched-task", status: "运行中" }],
      },
      dataCheck: { unmatchedTaskCount: 1, message: "1 条运行中任务在本次快照中暂未取得来源订单。这不代表任务异常，原因尚未查明。" },
      risk: { headline: "1 个订单有计划但未形成当前任务，原因尚未查明。", items: [{ orderNo: "O2", status: "未形成当前任务", reason: "原因尚未查明" }] },
      limitations: ["本场景为受控synthetic-demo，不代表实时业务库。"],
    },
  };
  await page.route("**/api/agent/chat/stream", route => route.fulfill({
    contentType: "text/event-stream",
    body: "event: delta\ndata: {\"text\":\"核对结论如下。\"}\n\nevent: ui_payload\ndata: " + JSON.stringify({ blocks: [block] }) + "\n\nevent: done\ndata: {\"toolCount\":1}\n\n",
  }));
  await page.goto("/");
  await page.getByRole("textbox", { name: "输入问题" }).fill("哪些订单已经形成派工");
  await page.getByRole("button", { name: "发送消息", exact: true }).click();
  await page.getByRole("button", { name: /查看详细/ }).click();
  await expect(page.getByText("订单到派工状态关系", { exact: true })).toBeVisible();
  await expect(page.getByText("按产线统计任务状态", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "全部订单", exact: true })).toBeVisible();
  await expect(page.getByText("来源核对提示", { exact: true })).toBeVisible();
  await expect(page.getByText("现场下发状态", { exact: true })).toBeVisible();
  await expect(page.getByText("关联当前任务数", { exact: true })).toBeVisible();
  await expect(page.getByText("当前核对状态", { exact: true })).toBeVisible();
  await expect(page.getByText("核对结论", { exact: true })).toBeVisible();
  await expect(page.getByText("taskTag", { exact: true })).toHaveCount(0);
  await expect(page.getByText("下发/流转状态", { exact: true })).toHaveCount(0);
  await expect(page.locator(".aps-analysis-viewer .aps-detail tbody tr").first()).toContainText("派工在制");
  await expect(page.getByText("受控演示 · 只读查看", { exact: true })).toBeVisible();
  await expect(page.getByText("风险说明", { exact: true })).toBeVisible();
  await page.locator(".aps-analysis-viewer .alignment-metrics button").filter({ hasText: "派工在制" }).click();
  await expect(page.getByText("派工在制订单", { exact: true })).toBeVisible();
  await page.locator(".alignment-filter-chips button").filter({ hasText: "任务来源未取得" }).click();
  await expect(page.getByText("T2", { exact: true })).toBeVisible();
  if (process.env.ORDER_DISPATCH_CAPTURE_PATH) await page.screenshot({ path: process.env.ORDER_DISPATCH_CAPTURE_PATH });
  expect(errors).toEqual([]);
});

test("viewing conversations does not change recent history order", async ({ page }) => {
  const conversations = [
    { id: "chat-a", title: "对话 A", updatedAt: 3000, summary: "", summarizedMessageCount: 0, messages: [{ id: "a-user", role: "user", content: "问题 A", createdAt: 3000 }, { id: "a-answer", role: "assistant", content: "回答 A", createdAt: 3001, status: "done" }] },
    { id: "chat-b", title: "对话 B", updatedAt: 2000, summary: "", summarizedMessageCount: 0, messages: [{ id: "b-user", role: "user", content: "问题 B", createdAt: 2000 }, { id: "b-answer", role: "assistant", content: "回答 B", createdAt: 2001, status: "done" }] },
    { id: "chat-c", title: "对话 C", updatedAt: 1000, summary: "", summarizedMessageCount: 0, messages: [{ id: "c-user", role: "user", content: "问题 C", createdAt: 1000 }, { id: "c-answer", role: "assistant", content: "回答 C", createdAt: 1001, status: "done" }] },
  ];
  await page.goto("/");
  await page.evaluate(async records => {
    localStorage.clear();
    await new Promise((resolve, reject) => {
      const request = indexedDB.deleteDatabase("wepilot");
      request.onsuccess = resolve;
      request.onerror = () => reject(request.error);
      request.onblocked = resolve;
    });
    await new Promise((resolve, reject) => {
      const request = indexedDB.open("wepilot", 1);
      request.onupgradeneeded = () => request.result.createObjectStore("conversations", { keyPath: "id" });
      request.onerror = () => reject(request.error);
      request.onsuccess = () => {
        const db = request.result;
        const transaction = db.transaction("conversations", "readwrite");
        for (const record of records) transaction.objectStore("conversations").put(record);
        transaction.oncomplete = () => { db.close(); resolve(); };
        transaction.onerror = () => reject(transaction.error);
      };
    });
  }, conversations);
  await page.reload();
  await expect(page.locator(".history-row")).toHaveCount(3);
  const historyOrder = () => page.locator(".history-row .history-open span").allTextContents();
  const openConversation = async title => {
    await page.locator(".history-open").filter({ hasText: title }).click();
    await page.waitForTimeout(80);
  };

  await expect.poll(historyOrder).toEqual(["对话 A", "对话 B", "对话 C"]);
  await openConversation("对话 B");
  await expect.poll(historyOrder).toEqual(["对话 A", "对话 B", "对话 C"]);
  await openConversation("对话 C");
  await expect.poll(historyOrder).toEqual(["对话 A", "对话 B", "对话 C"]);
  await openConversation("对话 A");
  await expect.poll(historyOrder).toEqual(["对话 A", "对话 B", "对话 C"]);
  await page.reload();
  await expect(page.locator(".history-row")).toHaveCount(3);
  await expect.poll(historyOrder).toEqual(["对话 A", "对话 B", "对话 C"]);
});
