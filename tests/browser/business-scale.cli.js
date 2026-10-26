async (page)=>{
 // Authored UI fixtures only. No native calculation, real business data or writes.
 const checks=[],requests=[],check=(ok,m)=>{if(!ok)throw Error(m);checks.push(m);};page.setDefaultTimeout(30000);await page.setViewportSize({width:1520,height:1080});
 const orders=Array.from({length:500},(_,i)=>({orderId:'Z'+String(i+1).padStart(7,'0'),product:'P'+String(i+1).padStart(4,'0'),quantity:100+i,taskTag:i%5===0?'T':'F',position:i+1,planStart:'2026-10-16',planEnd:'2026-10-21',dueDate:'2026-10-17',risk:true}));
 const baseline={schemaVersion:4,demandVersion:'SIM-261016-12A',revision:0,nativeResultId:'2147765ec3f748d297a5db8e09cfd58f',releaseStatus:'unreleased'};
 const make=(view='overview',rows=orders)=>{
  const spec={native:true,presentationVersion:2,view,orders:rows,allOrders:orders,orderId:'',prepare:false,headline:'本批订单可按订单号搜索并分页查看。',demandVersion:baseline.demandVersion,calculationId:baseline.nativeResultId,baseRevision:0,baseState:baseline,mrpRows:[],materialRows:[],tasks:[],changes:[],workspace:{native:true,view,orders:rows,focusOrderId:''},chart:{kind:'gantt',title:'工序计划',columns:[{key:'2026-10-16-d',date:'2026-10-16',shift:'白班'},{key:'2026-10-16-n',date:'2026-10-16',shift:'夜班'}],fixedColumns:[{key:'orderId',label:'订单号'},{key:'operation',label:'工序'}],rows:rows.map(o=>({key:o.orderId,orderId:o.orderId,operation:'A01',lineName:'L01',productName:o.product})),items:rows.map((o,i)=>({rowIndex:i,columnIndex:0,quantity:100,tone:'day'}))}};
  if(view==='material'||view==='mrp')spec.mrpRows=rows.map((o,i)=>({detailGuid:'material-'+o.orderId,orderId:o.orderId,material:'M-'+o.orderId,active:'',level:'001.001',rate:1,beforeOffset:100,stockUsed:10,transitUsed:20,remaining:70,demand:70,scrapRate:0,scrapNum:0,safeDemand:0,unit:i%2?'粒':'KG',needDate:'2026-10-16'}));
  return spec;
 };
 const handler=async(route)=>{
  const body=route.request().postDataJSON();requests.push(body.message);let spec;
  if(body.demoAction){await new Promise(resolve=>setTimeout(resolve,1800));spec=make('mrp',orders.slice(0,1));}
  else if(body.message==='展示规模验证500')spec=make();
  else if(body.message==='展示规模验证300')spec=make('material',orders.slice(0,300));
  else if(body.message==='目标选择规模验证'){spec=make('reschedule');spec.prepare=true;}
  else if(body.message==='取消等待验证'){spec=make('mrp',orders.slice(0,1));spec.prepare=true;}
  else if(body.message==='历史记录展示验证'){spec=make();spec.native=false;spec.proposedPlan={revision:1};spec.sourceLabel='LOCAL SIMULATION';}
  else {const id=body.message.match(/Z\d{7}/)?.[0];spec=make(body.message.includes('物料')?'material':'order',orders.filter(o=>o.orderId===id));spec.orderId=id||'';spec.workspace.focusOrderId=id||'';}
  const block={type:'interactive-demo',title:spec.prepare?'操作确认':'订单明细',spec},frames=[['delta',{text:spec.prepare?'请核对信息后确认。':'查询完成，可查看下方明细。'}],['ui_payload',{blocks:[block]}],['done',{modelInvoked:false,mode:'authored-ui-fixture',toolCalls:[]}]];
  try{await route.fulfill({status:200,contentType:'text/event-stream',body:frames.map(([event,data])=>'event: '+event+'\ndata: '+JSON.stringify(data)+'\n\n').join('')});}catch(e){if(!body.demoAction)throw e;}
 };
 await page.route('**/api/agent/chat/stream',handler);
 const cards=()=>page.locator('.message-list .native-demo'),detail=()=>page.getByRole('region',{name:'操作详情',exact:true});
 const ask=async(text)=>{const n=await cards().count();await page.getByRole('textbox',{name:'输入问题',exact:true}).fill(text);await page.getByRole('button',{name:'发送消息',exact:true}).click();await page.waitForFunction(n=>document.querySelectorAll('.message-list .native-demo').length>n,n);await page.getByRole('button',{name:'停止生成',exact:true}).waitFor({state:'hidden'});return cards().last();};
 try{
  if(await detail().count())await detail().getByRole('button',{name:'关闭操作详情',exact:true}).click();
  await page.getByRole('button',{name:'新建对话',exact:true}).click();await page.locator('.suggestion-grid').waitFor();let card=await ask('展示规模验证500');
  check(await card.locator('tbody tr').count()===5,'500单聊天仅生成五行DOM');check((await card.innerText()).includes('共 500 条'),'聊天总数保留500');const chatHeight=await card.evaluate(e=>Math.round(e.getBoundingClientRect().height));
  await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();let table=detail().getByRole('region',{name:'订单明细',exact:true});
  check(await table.locator('tbody tr').count()===20,'详情实际只生成20行DOM');check((await table.innerText()).includes('1 / 25'),'500条有25页');
  await detail().getByRole('button',{name:'查看工序排程',exact:true}).click();check(await detail().locator('.aps-gantt-wrap tbody tr').count()===20,'甘特仅展开当前页20单');
  await table.getByRole('button',{name:'订单明细下一页',exact:true}).click();check((await table.locator('tbody tr').first().innerText()).includes('Z0000021'),'下一页切换为第21单');check((await detail().locator('.aps-gantt-wrap tbody tr').first().innerText()).includes('Z0000021'),'甘特同步当前页而非始终第一页');
  for(let i=0;i<23;i++)await table.getByRole('button',{name:'订单明细下一页',exact:true}).click();
  check((await table.locator('tbody tr').first().innerText()).includes('Z0000481'),'末页显示481至500');check(await table.getByRole('button',{name:'订单明细下一页',exact:true}).isDisabled(),'末页不能越界');
  await table.getByRole('searchbox',{name:'搜索订单明细',exact:true}).fill('Z0000499');check(await table.locator('tbody tr').count()===1,'跨全量搜索定位499单');check((await table.innerText()).includes('全部 500 条'),'搜索保留全量计数');
  await table.getByRole('button',{name:'查看订单 Z0000499',exact:true}).click();check(await detail().locator('.aps-gantt-wrap tbody tr').count()===1,'选中行仅展开该单工序');
  await page.screenshot({path:'output/playwright/20261016-experience-fix/10-scale-500-search.png'});
  const beforeLinked=await cards().count();await detail().getByRole('button',{name:'核对物料',exact:true}).click();await page.waitForFunction(n=>document.querySelectorAll('.message-list .native-demo').length>n,beforeLinked);await page.getByRole('button',{name:'停止生成',exact:true}).waitFor({state:'hidden'});check(requests.at(-1).includes('Z0000499'),'关联提问带入选中第499单');card=cards().last();check((await card.innerText()).includes('Z0000499'),'关联卡仍是选中订单');
  card=await ask('展示规模验证500');await card.getByRole('button',{name:'查看明细',exact:true}).click();table=detail().getByRole('region',{name:'订单明细',exact:true});
  await table.getByRole('combobox',{name:'派工在制',exact:true}).selectOption('T');check((await table.innerText()).includes('共 100 条'),'筛选统计基于全部500条');check(await table.locator('tbody tr').count()===20,'筛选后仍按20条分页');
  await table.getByRole('searchbox',{name:'搜索订单明细',exact:true}).fill('no-such-order');check(await table.locator('tbody tr').count()===0,'空搜索不残留上页数据');check((await table.innerText()).includes('未找到匹配记录'),'空结果给出明确反馈');await page.keyboard.press('Escape');await detail().waitFor({state:'hidden'});
  card=await ask('展示规模验证300');check(await card.locator('tbody tr').count()===5,'300项物料聊天仍为五条');check((await card.innerText()).includes('共 300 条'),'物料总数保留300');
  await card.getByRole('button',{name:'查看明细',exact:true}).click();table=detail().getByRole('region',{name:'关键材料',exact:true});check(await table.locator('tbody tr').count()===20,'物料详情真实分页');check((await table.innerText()).includes('1 / 15'),'300项物料15页');check((await table.innerText()).includes('公斤')&&(await table.innerText()).includes('粒'),'不同计量单位逐项保留');
  await table.getByRole('searchbox',{name:'搜索关键材料',exact:true}).fill('Z0000299');check(await table.locator('tbody tr').count()===1,'物料全量检索第299项');await page.screenshot({path:'output/playwright/20261016-experience-fix/11-scale-300-material.png'});await page.keyboard.press('Escape');
  card=await ask('目标选择规模验证');check(await card.getByRole('combobox',{name:'目标订单',exact:true}).locator('option').count()<=32,'大批候选最多30条并可搜索');
  await card.getByRole('searchbox',{name:'搜索目标订单',exact:true}).fill('0499');await card.getByRole('combobox',{name:'目标订单',exact:true}).selectOption('Z0000499');check(await card.getByRole('combobox',{name:'目标订单',exact:true}).inputValue()==='Z0000499','候选检索可选第499单');await card.getByRole('button',{name:'取消',exact:true}).click();
  card=await ask('取消等待验证');const n=await cards().count();await card.getByRole('button',{name:'开始核算',exact:true}).click();await card.getByRole('button',{name:'查看明细',exact:true}).click();await detail().waitFor();check(await detail().getByRole('button',{name:'正在处理…',exact:true}).isDisabled(),'聊天开始计算后详情同步运行态');await detail().getByRole('button',{name:'取消等待',exact:true}).click();await detail().getByText('已取消等待，当前计划没有变化。',{exact:true}).waitFor();await detail().getByRole('button',{name:'关闭操作详情',exact:true}).click();await page.waitForTimeout(2100);check(await cards().count()===n,'从详情取消聊天计算后不追加迟到结果');check(await card.getByRole('button',{name:'开始核算',exact:true}).count()===0,'跨视图取消后确认表单保持关闭');
  card=await ask('历史记录展示验证');check(await card.locator('tbody tr').count()===5,'旧schema3历史记录也限制五条预览');check(!/SIMULATION|模拟|GUID|需求版本/.test(await card.innerText()),'旧schema3历史展示无开发信息');check(await card.locator('form').count()===0,'旧算法历史记录只读不再执行');
  await page.setViewportSize({width:1366,height:768});check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'规模样例在1366桌面无整页横向溢出');
  return {status:'PASS',fixtureOnly:true,checks,chatHeight,requests};
 }finally{await page.unroute('**/api/agent/chat/stream',handler);}
}
