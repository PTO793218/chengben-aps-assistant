import {test,expect} from '@playwright/test';
import fs from 'node:fs';
const legacy=JSON.parse(fs.readFileSync(new URL('../../agent-backend/Data/production-demo.json',import.meta.url)));
legacy.id='CB-DEMO-20260910-v1';delete legacy.capacity;delete legacy.hoursSource;
for(const task of legacy.tasks)delete task.plannedHours;

test('old chat calendar keeps planned cells visible and navigates to original shifts',async({page})=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/api/agent/chat/stream',route=>route.fulfill({contentType:'text/event-stream',body:
  `event: delta\ndata: ${JSON.stringify({text:'历史生产计划快照'})}\n\nevent: ui_payload\ndata: ${JSON.stringify({blocks:[{type:'production-plan',title:'生产计划概况',spec:{snapshot:legacy,focus:{scope:'all'}}}]})}\n\nevent: done\ndata: {"toolCount":1}\n\n`}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'输入问题'}).fill('生产计划怎么样？');
 await page.getByRole('button',{name:'发送消息',exact:true}).click();
 await expect(page.getByRole('region',{name:'生产计划概况'})).toBeVisible();
 await page.reload();
 const entry=page.getByRole('button',{name:'查看产线日历全景',exact:true});
 await expect(entry.locator('polygon[fill="#7496cc"]')).toHaveCount(52);
 await entry.click();
 await expect(page.locator('.production-calendar-notice')).toContainText('104 项已排班次');
 await expect(page.locator('.production-scene canvas')).toBeVisible();
 await page.screenshot({path:'../temp/screenshots/calendar-legacy-restored.png'});
 await page.getByLabel('产线筛选',{exact:true}).selectOption('turn-1');
 await page.getByLabel('日历定位日期').fill('2026-09-07');
 await page.getByLabel('日历定位日期').blur();
 await expect(page.locator('.production-day-count')).toContainText('工时数据不足');
 await expect(page.locator('.production-task-list button')).toHaveCount(2);
 await page.getByRole('button',{name:'查看这一天的订单排程'}).click();
 await expect(page.locator('.production-task-list button')).toHaveCount(2);
 await expect(page.locator('.production-task-list')).toContainText('白班');
 await expect(page.locator('.production-task-list')).toContainText('晚班');
 expect(errors).toEqual([]);
});
