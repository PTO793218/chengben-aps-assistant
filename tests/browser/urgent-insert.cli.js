async (page,data)=>{
 page.setDefaultTimeout(15000);await page.setViewportSize({width:1520,height:1080});
 const checks=[],errors=[],requests=[];let index=0;
 const check=(ok,label)=>{if(!ok)throw Error(label);checks.push(label);};
 page.on('pageerror',e=>errors.push(e.message));
 const cards=()=>page.locator('.message-list .native-demo'),detail=()=>page.getByRole('region',{name:'操作详情',exact:true});
 await page.route('**/api/agent/chat/stream',async route=>{
  try{
   const body=route.request().postDataJSON(),entry=data.records[index++];if(!entry)throw Error('Unexpected request');
   const spec=entry.result.uiPayload[0].spec;
   check(body.demoPlan.nativeResultId===spec.baseState.nativeResultId,'当前计划承接:'+entry.label);
   check(body.demoPlan.orderIds.join('/')===spec.baseState.orderIds.join('/'),'整批订单承接:'+entry.label);
   check(body.demoPlan.nativeMrpResultId===spec.baseState.nativeMrpResultId,'独立MRP承接:'+entry.label);
   if(body.demoAction)check(body.demoAction.action===spec.view&&body.demoAction.orderId===spec.orderId,'界面确认参数:'+entry.label);
   requests.push({label:entry.label,body});const event=(name,value)=>`event: ${name}\ndata: ${JSON.stringify(value)}\n\n`;
   await route.fulfill({status:200,contentType:'text/event-stream',body:event('delta',{text:entry.reply||entry.result.summary})+event('ui_payload',{blocks:entry.result.uiPayload})+event('done',{requestId:'offline-'+index,mode:'offline-verification',modelInvoked:false,toolCalls:[]})});
  }catch(e){errors.push(String(e));await route.abort();}
 });
 await page.getByRole('button',{name:'新建对话',exact:true}).click();await page.locator('.suggestion-grid').waitFor();
 check(await page.locator('.suggestion-grid button').count()===5,'首页仍有五个入口');
 check(await page.getByRole('button',{name:/来了急单，怎么安排/}).count()===1,'首页替换为新增急单入口');
 const ready=async n=>{await page.waitForFunction(n=>document.querySelectorAll('.message-list .native-demo').length>n,n);await page.getByRole('button',{name:'停止生成',exact:true}).waitFor({state:'hidden'});return cards().last();};
 const ask=async text=>{const n=await cards().count();await page.getByRole('textbox',{name:'输入问题',exact:true}).fill(text);await page.getByRole('button',{name:'发送消息',exact:true}).click();return ready(n);};
 const close=async()=>detail().getByRole('button',{name:'关闭操作详情',exact:true}).click();
 let card=await ask('当前生产计划怎么样？');check((await card.innerText()).includes('12'),'起始计划12单');
 check(await card.locator('tbody tr').count()===5,'聊天仍最多五条预览');
 card=await ask('业务刚来了一张急单，帮我看看怎么安排，先别改当前计划');
 check(await card.getByRole('button',{name:'开始试排',exact:true}).isDisabled(),'未选择新增单不能执行');
 const candidate=card.getByRole('combobox',{name:'新增急单',exact:true});await candidate.selectOption('Z99100014');
 check(await candidate.locator('option').count()===2,'只列尚未进入计划的新单及空选项');
 check(!(await candidate.innerText()).includes('Z9900010'),'已有订单不冒充新急单候选');
 check((await card.innerText()).includes('13'),'确认说明加入后13单');check((await card.innerText()).includes('1,050')&&(await card.innerText()).includes('2026-10-23'),'确认卡显示急单数量和交期');
 await card.getByRole('button',{name:'取消',exact:true}).click();check((await card.innerText()).includes('取消'),'取消不会执行计算');
 await card.getByRole('button',{name:'重新准备',exact:true}).click();
 await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();
 check(await detail().getByRole('combobox',{name:'新增急单',exact:true}).inputValue()==='Z99100014','聊天和详情共享新单选择');
 await page.screenshot({path:'output/playwright/20261019-urgent-insert/01-confirm.png'});await close();
 let n=await cards().count();await card.getByRole('button',{name:'开始试排',exact:true}).click();card=await ready(n);
 await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();
 check(await detail().locator('.business-table').first().locator('tbody tr').count()===13,'整批对比13单');
 const comparison=await detail().locator('.business-table').first().innerText();check(comparison.includes('Z99100014')&&comparison.includes('未入排')&&comparison.includes('新增订单'),'新增单明确显示原来未入排');
 await page.screenshot({path:'output/playwright/20261019-urgent-insert/02-comparison.png'});await close();
 await card.getByRole('button',{name:'采用此方案',exact:true}).click();await card.getByText('已采用此方案',{exact:true}).first().waitFor();
 card=await ask('现在的生产计划怎么样？');check((await card.innerText()).includes('13'),'采用后计划查询13单');
 card=await ask('现在有哪些订单有交期风险？');check(await card.locator('tbody tr').count()===Math.min(5,data.records.find(r=>r.label==='risk').result.uiPayload[0].spec.orders.length),'风险按采用后的计算结果显示');
 card=await ask('查一下 Z99100014 的物料情况');check((await card.innerText()).includes('尚未完成独立物料核算'),'旧12单MRP不伪装成新单结果');
 card=await ask('重新核算这批订单的物料需求，先看 Z99100014');n=await cards().count();await card.getByRole('button',{name:'开始核算',exact:true}).click();card=await ready(n);
 await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();check((await detail().innerText()).includes('Z99100014'),'MRP显示新单核算');
 await page.screenshot({path:'output/playwright/20261019-urgent-insert/03-mrp.png'});await close();await card.getByRole('button',{name:'保存核算结果',exact:true}).click();
 await ask('查一下 Z99100014 的物料核算依据');
 card=await ask('Z99100014 怎么安排？');check((await card.innerText()).includes('Z99100014'),'新增单可以查询排程');
 await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();check(await detail().locator('.aps-gantt-wrap tbody tr').count()>0,'新增单可查看实际工序矩阵');await page.screenshot({path:'output/playwright/20261019-urgent-insert/04-order.png'});await close();
 card=await ask('Z99100014 有派工了吗？');check((await card.innerText()).includes('0'),'不凭空生成派工');
 card=await ask('准备下发当前计划，先让我确认');check((await card.innerText()).includes('13'),'下发确认涵盖13单');
 n=await cards().count();await card.getByRole('button',{name:'确认下发',exact:true}).click();card=await ready(n);check((await card.innerText()).includes('计划下发完成'),'承接13单已展示计划后下发');await page.screenshot({path:'output/playwright/20261019-urgent-insert/05-release.png'});
 check(index===data.records.length,'全部操作使用当次真实业务工具结果');check(errors.length===0,'无浏览器脚本或请求错误');
 check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'无整页水平溢出');
 return {status:'PASS',checks,errors,interceptedModelRequests:requests.length,forwardedModelRequests:0,requests};
}
