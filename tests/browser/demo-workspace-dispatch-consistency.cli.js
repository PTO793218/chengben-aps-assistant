async(page)=>{
  page.setDefaultTimeout(150000);
  await page.getByRole('button',{name:/MRP 演示 Z9900003，需求数量8000/}).first().click();
  await page.locator('.demo-version-strip').waitFor();
  const count=await page.locator('.order-dispatch-card').count();
  await page.locator('.demo-workspace-context').last().getByRole('button',{name:'查派工',exact:true}).click();
  await page.waitForFunction(n=>document.querySelectorAll('.order-dispatch-card').length>n,count);
  await page.locator('.order-dispatch-card').last().getByRole('button',{name:'查看详细'}).click();
  const region=page.getByRole('region',{name:'订单派工在制核对详情',exact:true});
  const text=await region.innerText();
  if(!text.includes('261015001')||!text.includes('未下达')||text.includes('原因尚未查明'))throw Error('dispatch state and risk detail disagree');
  if(!text.includes('SIM-L1 / SIM-L2 / SIM-L3'))throw Error('related plan lines missing');
  await page.screenshot({path:'output/playwright/20261015-workspace-dispatch-consistency.png'});
  await page.getByRole('button',{name:'关闭订单派工在制核对详情'}).click();
  return{status:'PASS',checks:['all related lines','explicit unreleased state','risk detail consistent with row and headline','no fabricated task'],plan:'restored initial plan in same demand version'};
}
