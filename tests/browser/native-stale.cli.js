async(page)=>{
 await page.getByRole('button',{name:/^当前演示生产计划怎么样？/}).first().click();
 const adopt=page.getByRole('button',{name:'采用已展示的试排结果',exact:true});
 if(await adopt.count()!==1||!await adopt.isDisabled())throw Error('旧试排卡不能再次覆盖当前结果');
 if(!(await page.locator('.demo-version-strip').innerText()).includes('已本地试下发'))throw Error('方案状态未保留');
 const restore=page.getByRole('button',{name:'恢复基线计划',exact:true});
 for(let i=0;i<await restore.count();i++)if(!await restore.nth(i).isDisabled())throw Error('下发后仍可恢复');
 await page.getByRole('button',{name:'新建对话',exact:true}).click();await page.locator('.suggestion-grid').waitFor();
 if(!(await page.locator('.demo-version-strip').innerText()).includes('12A'))throw Error('未回到当前版本');
 return {status:'PASS',checks:['旧试排卡禁用','下发状态保留','下发后禁止恢复','新对话返回当前版本']};
}
