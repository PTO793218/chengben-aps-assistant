async(page,data)=>{
 const checks=[],errors=[];let blockedRequests=0;
 const check=(ok,label)=>{if(!ok)throw Error(label);checks.push(label);};
 const spec=label=>data.records.find(x=>x.label===label).result.uiPayload[0].spec;
 const block=label=>data.records.find(x=>x.label===label).result.uiPayload[0];
 const p13=spec('insert').proposedPlan,p14=spec('reschedule').proposedPlan;
 const contexts=[];
 const create=async()=>{const c=await page.context().browser().newContext({viewport:{width:1520,height:1080}});contexts.push(c);await c.route('**/api/agent/chat/stream',async r=>{blockedRequests++;await r.abort();});const p=await c.newPage();p.setDefaultTimeout(12000);p.on('pageerror',e=>errors.push(e.message));await p.goto('http://127.0.0.1:18440/');await p.getByRole('textbox',{name:'输入问题',exact:true}).waitFor();return p;};
 const conversation=(id,title,cards,plan,time=100)=>({id,title,updatedAt:time,messages:[{id:id+'u',role:'user',content:title,createdAt:time},...cards.map((b,i)=>({id:id+i,role:'assistant',content:'请核对这份方案。',status:'done',createdAt:time,uiBlocks:[b]}))],demoPlan:plan});
 const seed=async(p,plan,conversations)=>{
  await p.evaluate(async({plan,conversations})=>{
   const transact=(name,store,fn)=>new Promise((resolve,reject)=>{const r=indexedDB.open(name,1);r.onupgradeneeded=()=>{if(!r.result.objectStoreNames.contains(store))r.result.createObjectStore(store,{keyPath:'id'});};r.onsuccess=()=>{const db=r.result,t=db.transaction(store,'readwrite');fn(t.objectStore(store));t.oncomplete=()=>{db.close();resolve();};t.onerror=t.onabort=()=>reject(t.error);};});
   await transact('wepilot','conversations',s=>{s.clear();for(const c of conversations)s.put(c);});
   await transact('chengben-aps-workspace','plans',s=>{s.clear();if(plan!==null)s.put({id:'SIM-261019-13C',plan});});
  },{plan,conversations});await p.reload();await p.getByRole('textbox',{name:'输入问题',exact:true}).waitFor();
 };
 const plan=p=>p.evaluate(()=>new Promise(resolve=>{const r=indexedDB.open('chengben-aps-workspace',1);r.onsuccess=()=>{const db=r.result,t=db.transaction('plans'),q=t.objectStore('plans').getAll();t.oncomplete=()=>{db.close();resolve(q.result[0]?.plan);};};}));
 let p=await create();
 const committed=conversation('legacy13','已插入并采用13单',[block('insert')],p13,100);
 committed.messages.push({id:'commit13',role:'assistant',content:'已采用这次试排方案，后续查询将使用当前计划。',createdAt:100});
 const oldPlan={...spec('overview').baseState,revision:1},old=conversation('legacy12','旧对话后来查过12单',[block('overview')],oldPlan,999999);
 old.messages.push({id:'commit12',role:'assistant',content:'已采用这次试排方案，后续查询将使用当前计划。',createdAt:200});
 await seed(p,null,[committed,old]);check((await plan(p)).orderIds.length===13,'旧版本迁移保留已采用13单，不按最近聊天时间倒退');
 await p.reload();await p.getByRole('textbox',{name:'输入问题',exact:true}).waitFor();check((await plan(p)).orderIds.length===13,'迁移只执行一次，刷新保留共享计划');
 const undoConversation=conversation('undo','Z9900025 调整方案',[block('reschedule')]);await seed(p,p14,[undoConversation]);
 await p.locator('.message-list .native-demo').getByRole('button',{name:'查看明细',exact:true}).click();await p.getByRole('button',{name:'撤销本次采用',exact:true}).click();
 await p.waitForFunction(()=>document.body.innerText.includes('已撤销这次采用'));
 check((await plan(p)).revision===3&&(await plan(p)).orderIds.length===13&&(await plan(p)).nativeResultId===p13.nativeResultId,'撤销本次调序恢复13单，版本递增，未重置12单');
 await p.screenshot({animations:'disabled',path:'output/playwright/20261020-shared-plan/05-undo-keeps-thirteen.png'});
 // Real IDB transaction failure, injected only in this disposable context.
 await seed(p,p13,[conversation('failure','核对调序结果',[block('reschedule')])]);
 await p.evaluate(()=>{window.originalTransaction=IDBDatabase.prototype.transaction;IDBDatabase.prototype.transaction=function(...args){if(this.name==='chengben-aps-workspace'&&args[1]==='readwrite')throw Error('测试存储失败');return window.originalTransaction.apply(this,args);};});
 await p.getByRole('button',{name:'采用此方案',exact:true}).click();await p.getByRole('alert').filter({hasText:'测试存储失败'}).waitFor();
 check((await plan(p)).revision===1&&(await plan(p)).orderIds.length===13,'存储失败不显示采用成功且保留原13单');
 await p.reload();await p.getByRole('button',{name:'采用此方案',exact:true}).waitFor();await p.getByRole('button',{name:'采用此方案',exact:true}).click();await p.waitForFunction(()=>document.body.innerText.includes('已采用此方案'));
 check((await plan(p)).revision===2,'恢复存储后仅采用一次');
 // Two windows race on the same committed base. The shared IDB transaction must serialize commits.
 await seed(p,p13,[conversation('race','两个窗口核对同一调序',[block('reschedule')])]);
 const p2=await p.context().newPage();p2.setDefaultTimeout(12000);p2.on('pageerror',e=>errors.push(e.message));await p2.goto('http://127.0.0.1:18440/');await p2.getByRole('button',{name:'采用此方案',exact:true}).waitFor();
 await Promise.allSettled([p.getByRole('button',{name:'采用此方案',exact:true}).click(),p2.getByRole('button',{name:'采用此方案',exact:true}).click()]);
 await p.waitForFunction(()=>document.body.innerText.includes('已采用此方案'));check((await plan(p)).revision===2,'两个窗口同时采用只提交一个新版本');
 check((await plan(p2)).orderIds.length===13,'并发采用没有遗漏新增订单');
 await p2.close();
 await seed(p,{corrupted:true},[conversation('broken','数据损坏保护',[block('reschedule')])]);
 await p.locator('.notice').filter({hasText:'当前已保存计划无效'}).waitFor();
 check((await plan(p)).corrupted===true,'损坏的共享记录停止操作，不静默回退12单');
 check(await p.getByRole('button',{name:'采用此方案',exact:true}).count()===0,'共享计划无效时旧卡片不能采用');
 check(blockedRequests===0,'前述边界验收未发起模型请求');
 // Canceled and abandoned requests must not append or commit late results.
 await seed(p,p14,[conversation('cancel-owner','等待中的物料核算',[block('prepare-mrp')])]);
 await p.context().unroute('**/api/agent/chat/stream');let held=null,intercepted=0;
 const event=(name,value)=>`event: ${name}\ndata: ${JSON.stringify(value)}\n\n`;
 const respond=async(route,label)=>{const entry=data.records.find(x=>x.label===label);await route.fulfill({status:200,contentType:'text/event-stream',body:event('delta',{text:entry.reply||entry.result.summary})+event('ui_payload',{blocks:entry.result.uiPayload})+event('done',{modelInvoked:false,mode:'offline-verification'})}).catch(()=>{});};
 await p.context().route('**/api/agent/chat/stream',async r=>{held=r;intercepted++;});
 const waitHeld=async()=>{for(let i=0;i<100&&!held;i++)await new Promise(r=>setTimeout(r,20));check(!!held,'捕获请求，未外发');};
 await p.getByRole('button',{name:'开始核算',exact:true}).click();await waitHeld();await p.getByRole('button',{name:'取消等待',exact:true}).click();await respond(held,'mrp');held=null;
 await p.getByRole('button',{name:'重新准备',exact:true}).waitFor();check((await plan(p)).revision===2,'取消等待后迟到MRP不能保存');
 check(await p.getByRole('button',{name:'保存核算结果',exact:true}).count()===0,'取消后不追加迟到结果卡');
 await p.getByRole('button',{name:'重新准备',exact:true}).click();await p.getByRole('button',{name:'开始核算',exact:true}).click();await waitHeld();
 await p.getByRole('button',{name:'新建对话',exact:true}).click();await p.locator('.suggestion-grid').waitFor();await respond(held,'mrp');held=null;
 check(await p.locator('.message-list .native-demo').count()===0,'切换对话后迟到结果不进入新对话');check((await plan(p)).revision===2,'切换取消不改变已采用13单');
 // Release is not successful unless the canonical commit succeeds.
 await seed(p,spec('prepare-release').baseState,[conversation('release-failure','准备下发当前13单',[block('prepare-release')])]);
 await p.context().unroute('**/api/agent/chat/stream');await p.context().route('**/api/agent/chat/stream',async r=>{intercepted++;await respond(r,'release');});
 await p.evaluate(()=>{window.originalTransaction=IDBDatabase.prototype.transaction;IDBDatabase.prototype.transaction=function(...args){if(this.name==='chengben-aps-workspace'&&args[1]==='readwrite')throw Error('测试下发保存失败');return window.originalTransaction.apply(this,args);};});
 await p.getByRole('button',{name:'确认下发',exact:true}).click();await p.getByRole('alert').filter({hasText:'测试下发保存失败'}).waitFor();
 check((await plan(p)).releaseStatus==='unreleased'&&(await plan(p)).revision===3,'下发结果提交失败不改变当前状态');
 check(await p.getByText('计划下发完成',{exact:true}).count()===0,'提交失败不展示虚假的下发成功卡');
 await p.screenshot({animations:'disabled',path:'output/playwright/20261020-shared-plan/06-release-save-failure.png'});
check(errors.length===0,'边界验收无页面脚本错误');
 for(const c of contexts)await c.close();return {status:'PASS',checks,errors,interceptedModelRequests:intercepted,forwardedModelRequests:0};
}
