import {DEMAND_VERSION,initialDemoPlan,adoptDemoPlan,isCurrentNativePlan,nativePlanKey,samePlanBase} from './demo-plan.js';
const clone=x=>JSON.parse(JSON.stringify(x));
const checked=p=>{if(!isCurrentNativePlan(p))throw Error('当前已保存计划无效，已停止操作，未回退初始计划。');return clone(p);};
const cancelled=signal=>{if(signal?.aborted)throw Error('已取消此次操作，当前计划未改变。');};

// Recover committed conversation state only. A proposed UI card is never a plan.
export function recoverLegacyPlan(conversations=[]){
 const saved=conversations.filter(c=>isCurrentNativePlan(c.demoPlan)&&c.demoPlan.revision>0).map(c=>{
  const messages=(c.messages||[]).filter(m=>m.role==='assistant'&&m.status!=='error'&&m.status!=='interrupted');
  const commits=messages.filter(m=>/^已采用(?:这次|需求版本)|^已恢复(?:原方案|需求版本)|^本次物料核算已保存|^已保存独立MRP结果/.test(m.content||'')||(m.uiBlocks||[]).some(b=>b.spec?.receipt&&b.spec?.proposedPlan?.nativeReleaseId===c.demoPlan.nativeReleaseId));
  const restored=messages.filter(m=>/^已恢复(?:原方案|需求版本)/.test(m.content||''));
  return {plan:c.demoPlan,time:Math.max(0,...commits.map(m=>Number(m.createdAt)||0)),restore:Math.max(0,...restored.map(m=>Number(m.createdAt)||0))};
 });
 if(!saved.length)return initialDemoPlan();
 // A legacy twelve-order reordering is not evidence that the inserted order was removed.
 const maxOrders=Math.max(...saved.map(c=>c.plan.orderIds.length));
 const insertedAt=Math.max(0,...saved.filter(c=>c.plan.orderIds.length===maxOrders).map(c=>c.time));
 const eligible=saved.filter(c=>c.plan.orderIds.length===maxOrders||c.restore>insertedAt).sort((a,b)=>b.time-a.time);
 const latest=eligible[0];
 if(eligible.some(c=>c.time===latest.time&&nativePlanKey(c.plan)!==nativePlanKey(latest.plan)))throw Error('发现多个已保存计划，无法确认先后；已停止初始化，未覆盖任何计划。');
 return checked(latest.plan);
}

export function createWorkspacePlan(repository){
 const read=async()=>{const record=await repository.read();if(!record)throw Error('当前计划尚未初始化。');return checked(record.plan);};
 return {
  async init(conversations=[]){const record=await repository.transact(current=>current?{...current,plan:checked(current.plan)}:{id:DEMAND_VERSION,plan:recoverLegacyPlan(conversations),updatedAt:Date.now()});return checked(record.plan);},
  read,
  async apply(spec,{signal}={}){cancelled(signal);const record=await repository.transact(current=>{cancelled(signal);const plan=checked(current?.plan);return {id:DEMAND_VERSION,plan:adoptDemoPlan(plan,spec),updatedAt:Date.now()};},{signal});return checked(record.plan);},
  async undo(spec,{signal}={}){cancelled(signal);const record=await repository.transact(current=>{
   cancelled(signal);const plan=checked(current?.plan);
   if(plan.releaseStatus==='released'||!['insert','reschedule'].includes(spec?.view)||!samePlanBase(plan,spec.proposedPlan)||!isCurrentNativePlan(spec.baseState))throw Error('当前计划或核算已更新，不能用旧卡片恢复。');
   return {id:DEMAND_VERSION,plan:{...clone(spec.baseState),revision:plan.revision+1},updatedAt:Date.now()};
  },{signal});return checked(record.plan);}
 };
}

export function browserPlanRepository(){
 async function open(){return new Promise((resolve,reject)=>{
  let failed=false;const request=indexedDB.open('chengben-aps-workspace',1);
  request.onupgradeneeded=()=>{if(!request.result.objectStoreNames.contains('plans'))request.result.createObjectStore('plans',{keyPath:'id'});};
  request.onsuccess=()=>{if(failed)request.result.close();else resolve(request.result);};request.onerror=request.onblocked=()=>{failed=true;reject(Error('当前计划存储暂时不可用，请关闭重复窗口后重试。'));};
 });}
 async function transaction(mode,mutate,{signal}={}){
  cancelled(signal);const db=await open();
  return new Promise((resolve,reject)=>{
   let result,failure,tx;try{tx=db.transaction('plans',mode);}catch(e){db.close();reject(e);return;}const store=tx.objectStore('plans');
   const abort=()=>{failure=Error('已取消此次操作，当前计划未改变。');try{tx.abort();}catch{}};
   signal?.addEventListener('abort',abort,{once:true});
   const request=store.get(DEMAND_VERSION);
   request.onsuccess=()=>{try{cancelled(signal);result=mutate(request.result||null);if(mode==='readwrite')store.put(result);}catch(e){failure=e;tx.abort();}};
   const finish=()=>{signal?.removeEventListener('abort',abort);db.close();};
   tx.oncomplete=()=>{finish();resolve(result);};tx.onerror=tx.onabort=()=>{finish();reject(failure||Error('当前计划未能保存，原计划已保留，请重试。'));};
  });
 }
 return {read:()=>transaction('readonly',x=>x),transact:(fn,options)=>transaction('readwrite',fn,options)};
}
