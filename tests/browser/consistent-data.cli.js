async (page, data) => {
 page.setDefaultTimeout(15000);await page.setViewportSize({width:1520,height:1080});
 const checks=[],errors=[],requests=[];let index=0;
 page.on('pageerror',e=>errors.push(e.message));
 const check=(ok,label)=>{if(!ok)throw Error(label);checks.push(label);};
 const cards=()=>page.locator('.message-list .native-demo');
 const detail=()=>page.getByRole('region',{name:'操作详情',exact:true});
 await page.route('**/api/agent/chat/stream',async route=>{
  try{
   const body=route.request().postDataJSON(),entry=data.records[index++];
   if(!entry)throw Error('Unexpected model request');
   const spec=entry.result.uiPayload[0].spec;
   check(body.demoPlan.demandVersion===spec.demandVersion,'请求使用当前统一数据集：'+entry.label);
   check(body.demoPlan.nativeResultId===spec.baseState.nativeResultId,'请求承接当前计划：'+entry.label);
   check(body.demoPlan.nativeMrpResultId===spec.baseState.nativeMrpResultId,'请求承接已保存物料核算：'+entry.label);
   if(body.demoAction)check(body.demoAction.action===spec.view&&!spec.prepare,'确认操作与离线原过程结果一致：'+entry.label);
   requests.push({label:entry.label,demoPlan:body.demoPlan,demoAction:body.demoAction});
   const event=(name,value)=>`event: ${name}\ndata: ${JSON.stringify(value)}\n\n`;
   await route.fulfill({status:200,contentType:'text/event-stream',body:event('delta',{text:entry.result.summary})+event('ui_payload',{blocks:entry.result.uiPayload})+event('done',{requestId:'offline-'+index,mode:'offline-verification',modelInvoked:false,toolCalls:[]})});
  }catch(e){errors.push(String(e));await route.abort();}
 });
 await page.getByRole('button',{name:'新建对话',exact:true}).click();
 await page.locator('.suggestion-grid').waitFor();check(await page.locator('.suggestion-grid button').count()===5,'保留五个首页入口');
 await page.screenshot({path:'output/playwright/20261019-consistent-data/01-home.png'});
 const ready=async n=>{await page.waitForFunction(n=>document.querySelectorAll('.message-list .native-demo').length>n,n);await page.getByRole('button',{name:'停止生成',exact:true}).waitFor({state:'hidden'});return cards().last();};
 const ask=async text=>{const n=await cards().count();await page.getByRole('textbox',{name:'输入问题',exact:true}).fill(text);await page.getByRole('button',{name:'发送消息',exact:true}).click();return ready(n);};
 const close=async()=>detail().getByRole('button',{name:'关闭操作详情',exact:true}).click();
 let card=await ask('当前生产计划怎么样？');
 check(await card.locator('tbody tr').count()===5,'聊天保持五条预览');
 await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();
 const table=detail().locator('.business-table').first();check(await table.locator('tbody tr').count()===12,'明细展示同批十二单');
 check(!(await table.innerText()).includes('2026-07'),'订单明细不再混入7月历史交期');
 check((await table.innerText()).includes('2026-10-22')&&(await table.innerText()).includes('2026-10-29'),'交期落在统一模拟业务区间');
 await page.screenshot({path:'output/playwright/20261019-consistent-data/02-overview.png'});await close();
 card=await ask('哪些订单有交期风险？');check(await card.locator('tbody tr').count()===data.initialRiskCount,'风险名单等于原过程计算后逐单比较的结果');
 await page.screenshot({path:'output/playwright/20261019-consistent-data/03-risk-before.png'});
 card=await ask('帮我算一下 Z9900006 的物料需求');let n=await cards().count();await card.getByRole('button',{name:'开始核算',exact:true}).click();card=await ready(n);
 await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();
 check((await detail().innerText()).includes('6,020.7')&&(await detail().innerText()).includes('3,520.7'),'MRP展示同批输入计算出的抵扣和剩余需求');
 await detail().getByText(/查看计算依据/).click();await page.screenshot({path:'output/playwright/20261019-consistent-data/04-mrp.png'});await close();
 await card.getByRole('button',{name:'保存核算结果',exact:true}).click();
 await ask('查一下 Z9900006 的物料核算依据');
 card=await ask('把 Z9900010 调整到第1位试排，先让我确认');n=await cards().count();await card.getByRole('button',{name:'开始试排',exact:true}).click();card=await ready(n);
 await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();check(await detail().locator('.business-table').first().locator('tbody tr').count()===12,'试排对比涵盖整批十二单');
 await page.screenshot({path:'output/playwright/20261019-consistent-data/05-reschedule.png'});await close();await card.getByRole('button',{name:'采用此方案',exact:true}).click();
 await ask('再看看当前生产计划怎么样');card=await ask('采用以后哪些订单有交期风险？');
 check(await card.locator('tbody tr').count()===data.adoptedRiskCount,'采用后风险查询随当前计划更新');await page.screenshot({path:'output/playwright/20261019-consistent-data/06-risk-after.png'});
 await ask('Z9900010 现在怎么安排？');await ask('Z9900006 的物料核算依据是什么？');
 card=await ask('查一下 Z9900026 的派工情况');await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();
 check(await detail().locator('tbody tr').count()===6,'派工关联既有六条任务');await page.screenshot({path:'output/playwright/20261019-consistent-data/07-dispatch.png'});await close();
 card=await ask('准备下发当前计划，先让我确认');check((await card.innerText()).includes('计划下发确认'),'下发前先确认范围和日期');
 n=await cards().count();await card.getByRole('button',{name:'确认下发',exact:true}).click();card=await ready(n);
 check((await card.innerText()).includes('计划下发完成'),'原过程承接展示结果后才显示完成');await page.screenshot({path:'output/playwright/20261019-consistent-data/08-release.png'});
 await ask('再看看 Z9900026 的派工情况');
 check(index===data.records.length,'完整页面操作按顺序消费真实工具结果');check(errors.length===0,'浏览器无脚本或请求错误');
 check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'桌面无整页横向溢出');
 return {status:'PASS',checks,errors,interceptedModelRequests:requests.length,forwardedModelRequests:0,initialRiskCount:data.initialRiskCount,adoptedRiskCount:data.adoptedRiskCount,requests};
}
