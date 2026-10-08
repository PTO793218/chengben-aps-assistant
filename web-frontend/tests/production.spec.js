import {test,expect} from '@playwright/test';
import fs from 'node:fs';
const snapshot=JSON.parse(fs.readFileSync(new URL('../../agent-backend/Data/production-demo.json',import.meta.url)));
test.beforeEach(async({page})=>{
 await page.route('**/api/agent/chat/stream',route=>route.fulfill({contentType:'text/event-stream',body:
  `event: delta\ndata: ${JSON.stringify({text:'本地合成示例：12 个未完工订单，8 个已排完整、2 个部分已排、2 个尚未排入。点击图形查看全景。'})}\n\nevent: ui_payload\ndata: ${JSON.stringify({blocks:[{type:'production-plan',title:'生产计划概况',spec:{snapshot,focus:{scope:'all',orderId:'',lineId:'',date:''}}}]})}\n\nevent: done\ndata: {"toolCount":1}\n\n`}));
 await page.goto('/');
 await page.getByRole('textbox',{name:'输入问题'}).fill('生产计划怎么样？');
 await page.getByRole('button',{name:'发送消息',exact:true}).click();
 await expect(page.getByRole('region',{name:'生产计划概况'})).toBeVisible();
});
test('chat previews open only on click, filters and order followup survive return',async({page})=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await expect(page.getByRole('region',{name:'生产计划全景'})).toHaveCount(0);
 fs.mkdirSync('../temp/screenshots',{recursive:true});
 await page.screenshot({path:'../temp/screenshots/production-chat.png',fullPage:true});
 await page.reload();
 await expect(page.getByRole('region',{name:'生产计划概况'})).toBeVisible();
 const scroll=await page.locator('.message-scroll').evaluate(el=>el.scrollTop);
 await page.getByRole('button',{name:'查看订单排程全景',exact:true}).click();
 await expect(page.locator('.production-scene canvas')).toBeVisible();
 await page.waitForTimeout(450);
 await page.screenshot({path:'../temp/screenshots/production-schedule.png',fullPage:true});
 await page.getByLabel('订单筛选',{exact:true}).selectOption('Z9900001');
 await expect(page.locator('.production-count')).toHaveText('1 个订单');
 await page.getByRole('button',{name:'班次明细',exact:true}).click();
 await expect(page.locator('.production-task-list button')).toHaveCount(12);
 await page.locator('.production-task-list button').first().click();
 await expect(page.getByText('工段 / 工序',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'回到聊天，继续问这个订单',exact:true}).click();
 await expect(page.getByRole('textbox',{name:'输入问题'})).toHaveValue(/Z9900001/);
 await expect(page.getByRole('region',{name:'生产计划全景'})).toHaveCount(0);
 expect(await page.locator('.message-scroll').evaluate(el=>el.scrollTop)).toBeCloseTo(scroll,0);
 await page.getByRole('button',{name:'查看订单排程全景',exact:true}).click();
 await page.getByLabel('订单筛选',{exact:true}).selectOption('Z9900006');
 await page.getByRole('button',{name:'待排 3',exact:true}).click();
 await expect(page.locator('.production-pending-list button')).toHaveCount(3);
  await expect(page.locator('.production-empty-overlay')).toContainText('当前日期范围没有已排任务');
 await page.keyboard.press('Escape');
 await expect(page.getByRole('region',{name:'生产计划全景'})).toHaveCount(0);
 expect(errors).toEqual([]);
});
test('layered yearly calendar links a line and day to the same schedule tasks',async({page})=>{
 const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.getByRole('button',{name:'查看产线日历全景',exact:true}).click();
 await expect(page.locator('.production-scene canvas')).toBeVisible();
 await page.waitForTimeout(450);
 await page.screenshot({path:'../temp/screenshots/production-calendar.png',fullPage:true});
 await page.getByLabel('产线筛选',{exact:true}).selectOption('turn-1');
 await page.getByLabel('日历定位日期').fill('2026-01-01');
 await page.getByLabel('日历定位日期').blur();
 await expect(page.getByText('该日期没有样本数据，不能判断为空闲。')).toBeVisible();
 await page.getByLabel('日历定位日期').fill('2026-09-07');
 await page.getByLabel('日历定位日期').blur();
 await expect(page.locator('.production-day-count')).toContainText('60.0%');
 await page.getByRole('button',{name:'查看这一天的订单排程'}).click();
 await expect(page.getByLabel('开始日期')).toHaveValue('2026-09-07');
 await expect(page.getByLabel('结束日期')).toHaveValue('2026-09-07');
 await expect(page.locator('.production-task-list button')).toHaveCount(2);
 await page.getByRole('button',{name:'清除筛选',exact:true}).click();
 await expect(page.getByLabel('产线筛选',{exact:true})).toHaveValue('');
 await page.getByRole('button',{name:'关闭全景',exact:true}).click();
 await expect(page.getByRole('region',{name:'生产计划概况'})).toBeVisible();
 expect(errors).toEqual([]);
});
test('narrow screen retains preview and panorama controls without horizontal page overflow',async({page})=>{
 await page.setViewportSize({width:650,height:850});
 await page.getByRole('button',{name:'查看订单排程全景',exact:true}).click();
 await expect(page.getByRole('button',{name:'关闭全景',exact:true})).toBeVisible();
 await expect(page.locator('.production-scene canvas')).toBeVisible();
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
});

test('both scenes provide readable plane labels, separate shifts and optional rotation snapping',async({page})=>{
 await page.getByRole('button',{name:'查看订单排程全景',exact:true}).click();
 await expect(page.getByLabel('任务显示粒度')).toHaveCount(0);
 await expect(page.getByLabel('层间距离')).toHaveValue('2');
 const scene=page.locator('.production-scene');
 for(const mode of ['订单排程','年度日历']){
  await page.getByRole('button',{name:mode,exact:true}).click();
  for(const [name,view] of [['俯视','top'],['正视','front'],['侧视','side']]){
   await page.getByRole('button',{name,exact:true}).click();
   await expect(scene).toHaveAttribute('data-view',view);
   await expect(page.locator('.production-axis-label').first()).toBeVisible();
   const labels=await page.locator('.production-axis-label').evaluateAll(nodes=>nodes.map(n=>{
    const r=n.getBoundingClientRect();return {x:r.x,y:r.y,right:r.right,bottom:r.bottom,font:getComputedStyle(n).fontSize};
   }));
   expect(labels.length).toBeGreaterThan(1);
   for(let i=0;i<labels.length;i++){
    expect(labels[i].font).toBe('12px');
    for(let j=i+1;j<labels.length;j++)expect(labels[i].x<labels[j].right&&labels[i].right>labels[j].x&&labels[i].y<labels[j].bottom&&labels[i].bottom>labels[j].y).toBe(false);
   }
   await page.screenshot({path:`../temp/screenshots/planes-${mode==='订单排程'?'schedule':'calendar'}-${view}.png`});
  }
 }
 await page.getByRole('button',{name:'正视',exact:true}).click();
 const box=await scene.boundingBox();
 const drag=async()=>{await page.mouse.move(box.x+box.width*.55,box.y+box.height*.5);await page.mouse.down();await page.mouse.move(box.x+box.width*.55+7,box.y+box.height*.5,{steps:3});await page.mouse.up();};
 await drag();
 await expect(scene).toHaveAttribute('data-view','front');
 await page.getByLabel('自动吸附平面').uncheck();
 await drag();
 await page.waitForTimeout(500);
 await expect(scene).toHaveAttribute('data-view','free');
});

test('shift hover provides material fields and spacing moves labels without fitting back',async({page})=>{
 await page.getByRole('button',{name:'查看订单排程全景',exact:true}).click();
 await page.getByLabel('订单筛选',{exact:true}).selectOption('Z9900001');
 await page.getByLabel('产线筛选',{exact:true}).selectOption('turn-1');
 await page.getByLabel('开始日期').fill('2026-09-07');
 await page.getByLabel('结束日期').fill('2026-09-07');
 await page.getByLabel('结束日期').blur();
 await page.getByRole('button',{name:'俯视',exact:true}).click();
 const canvas=page.locator('.production-scene canvas'),box=await canvas.boundingBox();
 let hit=false;
 for(let y=box.y+20;y<box.y+box.height-20&&!hit;y+=24){
  for(let x=box.x+20;x<box.x+box.width-20&&!hit;x+=24){
   await page.mouse.move(x,y);
   hit=await page.locator('.production-tooltip').isVisible();
  }
 }
 expect(hit).toBe(true);
 const tip=page.locator('.production-tooltip');
 await expect(tip).toContainText('物料编码');await expect(tip).toContainText('部件品名');
 await expect(tip).toContainText('球面滚子 RQ03-23256');await expect(tip).toContainText('计划占用工时');
 await page.screenshot({path:'../temp/screenshots/shift-hover-details.png'});
 await page.mouse.move(80,100);
 await page.getByRole('button',{name:'清除筛选',exact:true}).click();
 await page.getByRole('button',{name:'侧视',exact:true}).click();
 const label=page.locator('.production-axis-label').filter({hasText:'油炉线 A'});
 await expect(label).toBeVisible();
 const before=await label.boundingBox();
 await page.getByLabel('产线间距',{exact:true}).fill('2.2');
 await page.waitForTimeout(100);
 const after=await label.boundingBox();
 expect(Math.abs(after.x-before.x)).toBeGreaterThan(3);
 await expect(page.locator('.production-scene')).toHaveAttribute('data-view','side');
});
