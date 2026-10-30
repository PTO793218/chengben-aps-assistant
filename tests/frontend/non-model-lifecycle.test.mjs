import test from 'node:test';
import assert from 'node:assert/strict';
import {setupSfc,vue} from './helpers/sfc-setup.mjs';
import {createPinia,setActivePinia} from '../../web-frontend/node_modules/pinia/dist/pinia.mjs';
import {useChatStore} from '../../web-frontend/src/stores/chat.js';
import {usePlanStore} from '../../web-frontend/src/stores/plan.js';
import {initialDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
import {nativeDemo,nativeDatasetIdentity} from '../../web-frontend/src/services/native-dataset.js';
import {createWorkspacePlan} from '../../web-frontend/src/services/workspace-plan.js';

const memory=new Map(),clone=x=>JSON.parse(JSON.stringify(x));
globalThis.localStorage={getItem:k=>memory.get(k)||null,setItem:(k,v)=>memory.set(k,v),removeItem:k=>memory.delete(k)};
globalThis.confirm=()=>true;
globalThis.window={removeEventListener(){}};
globalThis.document={querySelector:()=>null,activeElement:null,removeEventListener(){}};
const healthy=async()=>({status:'ok',mode:'model',dataset:nativeDatasetIdentity});

function proposal(view='insert'){
 const p=initialDemoPlan(),next={...clone(p),revision:p.revision+1};
 if(view==='mrp')next.nativeMrpResultId='b'.repeat(32);
 else{next.nativeResultId='a'.repeat(32);next.orderIds=[nativeDemo.insertionOrders[0].orderId,...p.orderIds];}
 return {native:true,view,demandVersion:p.demandVersion,baseRevision:p.revision,baseState:p,proposedPlan:next,calculationId:next.nativeResultId,mrpResultId:next.nativeMrpResultId,orderId:view==='insert'?next.orderIds[0]:'',orders:[],allOrders:[]};
}

async function setup(t,kind,{delayed=true,verify=healthy}={}){
 setActivePinia(createPinia());const store=useChatStore();
 store.current.messages.push({role:'user',content:'操作测试'});await store.persist();const owner=store.current.id;
 await store.create();store.current.messages.push({role:'user',content:'另一个对话'});await store.persist();const other=store.current.id;await store.select(owner);
 const spec=proposal(kind==='save-mrp'?'mrp':'insert');let saved=clone(kind==='undo'?spec.proposedPlan:spec.baseState),resumes=[];
 let entered;const started=new Promise(resolve=>entered=resolve);
 const workspace=createWorkspacePlan({async read(){return {plan:saved};},async transact(fn){
  entered();if(delayed)await new Promise(resolve=>resumes.push(resolve));const record=fn({plan:saved});saved=record.plan;return record;
 }});
 const planStore=vue.reactive({current:clone(saved),ready:true,async refresh(){return this.current=await workspace.read();},async apply(s,o){return this.current=await workspace.apply(s,o);},async undo(s,o){return this.current=await workspace.undo(s,o);}});
 const app=await setupSfc('web-frontend/src/App.vue',{}, {useChatStore:()=>store,usePlanStore:()=>planStore,verifyDatasetConnection:verify,createVoiceCapture:()=>({cancel(){}}),stopVolcTts:()=>{}});t.after(()=>app.unmount());
 const block=vue.reactive({type:'interactive-demo',spec});store.current.messages.push({role:'assistant',content:'结果',uiBlocks:[block]});
 const props={block,currentPlan:planStore.current,conversationId:owner,applyPlan:app.state.applyDemoPlan,saveMrp:app.state.saveMrpDemand,restorePlan:app.state.restoreDemoPlan,acceptResult:app.state.appendDemoResult,resolvePlan:app.state.resolveCurrentPlan};
 const card=await setupSfc('web-frontend/src/components/NativeDemoCard.vue',props);t.after(()=>card.unmount());
 return {app,card,store,owner,other,spec,props,planStore,started,read:()=>clone(saved),resume(){for(const r of resumes.splice(0))r();},pending:()=>resumes.length};
}

for(const action of ['adopt','save-mrp','undo'])for(const navigation of ['remove','create','select','clear','unmount']){
 test(`${action} cannot commit after ${navigation} before the storage transaction`,async t=>{
  const c=await setup(t,action),before=c.read();
  const work=action==='undo'?c.card.state.restore():c.card.state.adopt();await c.started;
  try{
   if(navigation==='remove')await c.app.state.removeConversation(c.store.current);
   if(navigation==='create')await c.app.state.createConversation();
   if(navigation==='select')await c.app.state.selectConversation(c.other);
   if(navigation==='clear')await c.app.state.clearConversations();
   if(navigation==='unmount')c.card.unmount();
  }finally{c.resume();await work;}
  assert.equal(c.read().revision,before.revision,'navigation must cancel the pending transaction, not only its notification');
  assert.deepEqual(c.read(),before);
 });
}

test('two undo clicks share loading and start only one transaction',async t=>{
 const c=await setup(t,'undo');const first=c.card.state.restore();await c.started;const second=c.card.state.restore();
 const count=c.pending();c.resume();await Promise.all([first,second]);assert.equal(count,1);
});

test('closing only the detail keeps an adoption shared with the chat card running',async t=>{
 const c=await setup(t,'adopt');const compact=await setupSfc('web-frontend/src/components/NativeDemoCard.vue',{...c.props,compact:true});t.after(()=>compact.unmount());
 const work=c.card.state.adopt();await c.started;c.card.unmount();c.resume();await work;
 assert.deepEqual(c.read(),c.spec.proposedPlan);
});

test('a committed adoption is retained when navigating while its notice is being saved',async t=>{
 const c=await setup(t,'adopt',{delayed:false});let resume,entered;const noticeStarted=new Promise(resolve=>entered=resolve),append=c.store.appendResult;
 c.store.appendResult=async(...args)=>{entered();await new Promise(resolve=>resume=resolve);return append(...args);};
 const work=c.card.state.adopt();await noticeStarted;assert.deepEqual(c.read(),c.spec.proposedPlan);
 await c.app.state.selectConversation(c.other);resume();await work;
 assert.deepEqual(c.read(),c.spec.proposedPlan);assert.equal(c.props.block.uiState.cancelled===true,false,'a successful commit must not be labelled cancelled');
 assert.match(c.store.findConversation(c.owner).messages.at(-1).content,/已采用/);
});

test('dataset mismatch blocks adopting, saving, undoing and calculation preflight',async t=>{
 const mismatch=async()=>{const e=new Error('页面数据版本已更新，请刷新页面后重试。');e.code='DATASET_REFRESH_REQUIRED';e.refreshRequired=true;throw e;};
 for(const kind of ['adopt','save-mrp','undo']){
  const c=await setup(t,kind,{delayed:false,verify:mismatch}),before=c.read();
  if(kind==='undo')await c.card.state.restore();else await c.card.state.adopt();
  assert.equal(c.read().revision,before.revision);assert.deepEqual(c.read(),before);assert.match(c.card.state.error.value,/刷新页面/);
  await assert.rejects(()=>c.app.state.resolveCurrentPlan(),/刷新页面/);
 }
});

test('identity is checked again after an asynchronous preflight before a local mutation',async t=>{
 let entered=false,resume;
 const c=await setup(t,'adopt',{delayed:false,verify:async()=>{entered=true;await new Promise(resolve=>resume=resolve);return healthy();}}),before=c.read();
 const work=c.card.state.adopt();for(let n=0;n<50&&!entered;n++)await Promise.resolve();
 assert.equal(entered,true,'local mutation must await dataset verification before committing');
 await c.app.state.removeConversation(c.store.current);resume();await work;
 assert.deepEqual(c.read(),before);
});

test('the real Pinia plan store forwards cancellation to undo',async()=>{
 setActivePinia(createPinia());const store=usePlanStore(),controller=new AbortController();controller.abort('user');
 await assert.rejects(()=>store.undo(proposal(),{signal:controller.signal}),/取消/);
});

test('health mismatch is visible and blocks a user query before sending to the model endpoint',async t=>{
 const c=await setup(t,'adopt',{delayed:false,verify:async()=>{throw Object.assign(new Error('页面数据与服务数据不一致，请刷新页面后重试。'),{code:'DATASET_REFRESH_REQUIRED',refreshRequired:true});}});
 const original=globalThis.fetch;let calls=0;globalThis.fetch=async()=>{calls++;throw Error('must not send');};t.after(()=>globalThis.fetch=original);
 await c.app.state.checkHealth();assert.equal(c.app.state.health.value,null);assert.equal(c.app.state.healthError.value,true);assert.match(c.app.state.notice.value,/刷新页面/);
 await c.app.state.performSend('生产计划怎么样？');assert.equal(calls,0);assert.match(c.store.current.messages.at(-1).error,/刷新页面/);
});

function releaseResponse(plan){
 const next={...clone(plan),revision:plan.revision+1,releaseStatus:'released',nativeReleaseId:'f'.repeat(32),releasedCalculationId:plan.nativeResultId};
 const spec={native:true,view:'release',orderId:'',demandVersion:plan.demandVersion,baseState:plan,baseRevision:plan.revision,orders:[],allOrders:[],proposedPlan:next,receipt:{sourceResultId:plan.nativeResultId}};
 return new Response([['delta',{text:'计划已下发'}],['ui_payload',{blocks:[{type:'interactive-demo',spec}]}],['done',{modelInvoked:false,mode:'test'}]].map(([event,data])=>`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`).join(''));
}

for(const navigation of ['remove','create','select','clear','unmount'])test(`release cannot commit after ${navigation} during local receipt storage`,async t=>{
 const c=await setup(t,'release'),before=c.read();
 c.props.block.spec={native:true,view:'release',prepare:true,orderId:'',demandVersion:before.demandVersion,baseState:before,baseRevision:before.revision,orders:[],allOrders:[]};
 const original=globalThis.fetch;globalThis.fetch=async()=>releaseResponse(before);t.after(()=>globalThis.fetch=original);
 const work=c.card.state.calculate();await c.started;
 if(navigation==='remove')await c.app.state.removeConversation(c.store.current);
 if(navigation==='create')await c.app.state.createConversation();
 if(navigation==='select')await c.app.state.selectConversation(c.other);
 if(navigation==='clear')await c.app.state.clearConversations();
 if(navigation==='unmount')c.card.unmount();
 c.resume();await work;assert.deepEqual(c.read(),before);
});

test('a committed release stays successful when navigating during notice persistence',async t=>{
 const c=await setup(t,'release',{delayed:false}),before=c.read();
 c.props.block.spec={native:true,view:'release',prepare:true,orderId:'',demandVersion:before.demandVersion,baseState:before,baseRevision:before.revision,orders:[],allOrders:[]};
 const original=globalThis.fetch;globalThis.fetch=async()=>releaseResponse(before);t.after(()=>globalThis.fetch=original);
 let entered,resume;const waiting=new Promise(resolve=>entered=resolve),append=c.store.appendResult;
 c.store.appendResult=async(...args)=>{entered();await new Promise(resolve=>resume=resolve);return append(...args);};
 const work=c.card.state.calculate();await waiting;assert.equal(c.read().releaseStatus,'released');
 await c.app.state.selectConversation(c.other);resume();await work;
 assert.equal(c.read().releaseStatus,'released');assert.equal(c.props.block.uiState.cancelled===true,false);
 assert.equal(c.store.findConversation(c.owner).messages.at(-1).uiBlocks[0].spec.receipt.sourceResultId,before.nativeResultId);
});

for(const action of ['remove','clear'])test(`failed ${action} keeps its current detail available with the retained conversation`,async t=>{
 const c=await setup(t,'adopt',{delayed:false}),detail={type:'interactive-demo',block:c.props.block};c.app.state.viewer.value=detail;
 c.store[action]=async()=>{c.store.persistenceWarning='存储不可用，尚未删除，请重试。';return false;};
 if(action==='remove')await c.app.state.removeConversation(c.store.current);else await c.app.state.clearConversations();
 assert.equal(c.app.state.viewer.value?.block,c.props.block);assert.equal(c.store.current.id,c.owner);
});

test('a local mutation timeout aborts its uncommitted transaction',async t=>{
 const c=await setup(t,'adopt'),before=c.read(),set=globalThis.setTimeout,clear=globalThis.clearTimeout;let expire;
 globalThis.setTimeout=(fn,ms,...args)=>ms===180000?(expire=fn,123456):set(fn,ms,...args);
 globalThis.clearTimeout=id=>{if(id!==123456)clear(id);};t.after(()=>{globalThis.setTimeout=set;globalThis.clearTimeout=clear;});
 const work=c.card.state.adopt();await c.started;
 try{assert.equal(typeof expire,'function','local saves must have a bounded wait');expire();}finally{c.resume();await work;}
 assert.deepEqual(c.read(),before);assert.match(c.card.state.error.value,/超时/);
});

test('an older successful health response cannot hide a newer dataset mismatch',async t=>{
 const pending=[];const c=await setup(t,'adopt',{verify:()=>new Promise((resolve,reject)=>pending.push({resolve,reject}))});
 const first=c.app.state.checkHealth(),second=c.app.state.checkHealth();
 pending[1].reject(Object.assign(new Error('页面数据已更新，请刷新页面后重试。'),{refreshRequired:true}));await second;
 pending[0].resolve(await healthy());await first;
 assert.equal(c.app.state.health.value,null);assert.equal(c.app.state.datasetRefreshRequired.value,true);
});

test('a mutation cannot use an older health result after a newer mismatch is known',async t=>{
 let first;const c=await setup(t,'adopt',{delayed:false,verify:()=>first?Promise.reject(Object.assign(new Error('请刷新页面后重试。'),{refreshRequired:true})):new Promise(resolve=>first=resolve)}),before=c.read();
 const work=c.card.state.adopt();for(let n=0;n<20&&!first;n++)await Promise.resolve();assert.equal(typeof first,'function');
 await c.app.state.checkHealth();first(await healthy());await work;
 assert.equal(c.read().revision,before.revision);assert.match(c.card.state.error.value,/刷新页面/);
});

for(const [view,phase] of [['insert','adopted'],['mrp','saved'],['release','released']])test(`refreshing a stale pending UI cannot hide an authoritative ${phase} state`,async t=>{
 let spec=proposal(view==='mrp'?'mrp':'insert');
 if(view==='release'){
  const plan=initialDemoPlan();spec={native:true,view:'release',baseState:plan,baseRevision:0,demandVersion:plan.demandVersion,orders:[],allOrders:[],receipt:{sourceResultId:plan.nativeResultId},proposedPlan:{...plan,revision:1,releaseStatus:'released',nativeReleaseId:'e'.repeat(32),releasedCalculationId:plan.nativeResultId}};
 }
 const card=await setupSfc('web-frontend/src/components/NativeDemoCard.vue',{block:{spec,uiState:{pending:true}},currentPlan:spec.proposedPlan,conversationId:'restored-'+phase});t.after(()=>card.unmount());
 assert.equal(card.state.phase.value.key,phase);
});

test('a cancelled preparation whose base changed offers the current-plan recovery state',async t=>{
 const p=initialDemoPlan(),spec={native:true,view:'mrp',prepare:true,baseState:p,baseRevision:0,orders:[],allOrders:[]};
 const card=await setupSfc('web-frontend/src/components/NativeDemoCard.vue',{block:{spec,uiState:{cancelled:true}},currentPlan:{...p,revision:1,nativeMrpResultId:'e'.repeat(32)},conversationId:'stale-cancel'});t.after(()=>card.unmount());
 assert.equal(card.state.phase.value.key,'superseded');
});
