async page=>{
 const checks=[],check=(ok,m)=>{if(!ok)throw Error(m);checks.push(m);};
 await page.reload();await page.setViewportSize({width:1520,height:1080});
 await page.getByRole('button',{name:/^当前生产计划怎么样？/}).first().click();await page.locator('.message-list .native-demo').first().waitFor();
 const font=await page.locator('.message.assistant .message-content').first().evaluate(e=>Number.parseFloat(getComputedStyle(e).fontSize));check(font>=14,'最终正文14px生效');
 await page.locator('.message-scroll').evaluate(e=>e.scrollTop=0);await page.screenshot({path:'output/playwright/20261016-experience-fix/02-overview.png'});
 const cards=page.locator('.message-list .native-demo'),detail=page.getByRole('region',{name:'操作详情',exact:true});await cards.first().getByRole('button',{name:'查看明细',exact:true}).click();await detail.waitFor();
 check(await detail.getByRole('button',{name:'恢复原方案',exact:true}).isDisabled(),'下发后历史总览不能恢复');
 await detail.getByRole('searchbox',{name:'搜索订单明细',exact:true}).fill('Z9900010');await detail.getByRole('button',{name:'查看订单 Z9900010',exact:true}).click();await detail.locator('.aps-gantt').waitFor();
 check(!/原过程|隔离|产品名称/.test(await detail.locator('.gantt-section').innerText()),'最终工序图使用业务标题及编号');await page.screenshot({path:'output/playwright/20261016-experience-fix/03-order-detail.png'});await detail.getByRole('button',{name:'关闭操作详情',exact:true}).click();
 const mrp=cards.filter({has:page.getByRole('heading',{name:'物料需求计算结果',exact:true})});check((await mrp.innerText()).includes('已保存核算结果'),'刷新后物料保存状态正确');check(await mrp.getByRole('button',{name:'保存核算结果',exact:true}).count()===0,'刷新后不能重复保存旧核算');
 const reschedule=cards.filter({has:page.getByRole('heading',{name:'试排结果对比',exact:true})});check((await reschedule.innerText()).includes('已采用此方案'),'刷新后方案采用状态正确');check(await reschedule.getByRole('button',{name:'采用此方案',exact:true}).count()===0,'下发后不能再次采用旧方案');
 await mrp.getByRole('button',{name:'查看明细',exact:true}).click();await detail.waitFor();await detail.getByText('查看完整需求分解',{exact:false}).click();await detail.getByRole('region',{name:'需求分解',exact:true}).waitFor();await page.screenshot({path:'output/playwright/20261016-experience-fix/04-material-detail.png'});await detail.getByRole('button',{name:'关闭操作详情',exact:true}).click();
 await page.getByRole('button',{name:'新建对话',exact:true}).click();await page.locator('.suggestion-grid').waitFor();check(await page.locator('.suggestion-grid button').count()===5,'验收后新对话回到五入口');await page.screenshot({path:'output/playwright/20261016-experience-fix/01-home.png'});
 return {status:'PASS',checks,font};
}
