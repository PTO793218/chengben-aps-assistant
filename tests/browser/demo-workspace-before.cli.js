async (page) => {
  page.setDefaultTimeout(150000);await page.setViewportSize({width:1440,height:1080});
  const check=(v,m)=>{if(!v)throw Error(m);};
  const ask=async text=>{await page.getByRole('textbox',{name:'输入问题',exact:true}).fill(text);await page.getByRole('button',{name:'发送消息',exact:true}).click();};
  await page.getByRole('button',{name:'新建对话',exact:true}).click();
  await page.locator('.suggestion-grid').waitFor();
  await ask('MRP 演示 Z9900003，需求数量8000，无库存无在途');
  let card=page.locator('.interactive-demo').last();await card.getByRole('button',{name:'计算演示需求'}).waitFor();
  check(await card.locator('.demo-execution').innerText().then(t=>t.includes('大模型已参与')),'model prepares input');
  await card.getByLabel('库存与在途场景').selectOption('late');
  await card.getByRole('button',{name:'计算演示需求'}).click();
  card=page.locator('.interactive-demo').last();await card.getByText('1,907.12',{exact:false}).first().waitFor();
  check(await card.innerText().then(t=>t.includes('2026-10-03')&&!t.includes('基准 r')),'MRP date linked and internal revision hidden');
  await card.getByRole('button',{name:'查看演示详情'}).click();
  const detail=page.getByRole('region',{name:'操作演示详情'});
  check(await detail.locator('tbody tr').count()===4,'BOM hierarchy retained');
  await detail.getByRole('button',{name:'保存模拟需求'}).click();
  await page.getByText('已将 Z9900003 的 8000 件模拟需求保存到需求版本 261015001',{exact:false}).waitFor();
  await page.locator('.demo-workspace-context').last().getByRole('button',{name:'查物料',exact:true}).click();
  const material=page.locator('.material-card').last();await material.getByRole('button',{name:'查看详情'}).waitFor();
  check(await material.innerText().then(t=>t.includes('1907.12')&&t.includes('8000')&&t.includes('12500')),'original material query uses saved partial demand, not changed order quantity');
  await material.getByRole('button',{name:'查看详情'}).click();
  const materialDetail=page.getByRole('region',{name:'订单物料齐套详情',exact:true});
  check(await materialDetail.innerText().then(t=>t.includes('1,907.12')&&t.includes('261015001')),'original material detail linked');
  await page.screenshot({path:'output/playwright/20261015-workspace-material-before.png'});
  await materialDetail.getByRole('button',{name:'关闭物料齐套详情'}).click();
  return {status:'PASS',checks:['numeric demand version','model preparation and calculation','plan-derived need date 10-03','save demand','original material query and detail','partial demand kept separate from order quantity'],userAgent:await page.evaluate(()=>navigator.userAgent)};
}
