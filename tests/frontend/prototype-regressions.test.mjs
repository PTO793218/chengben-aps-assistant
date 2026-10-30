import test from 'node:test';
import assert from 'node:assert/strict';
import {setupSfc,vue} from './helpers/sfc-setup.mjs';
import {createPinia,setActivePinia} from '../../web-frontend/node_modules/pinia/dist/pinia.mjs';
import {useChatStore} from '../../web-frontend/src/stores/chat.js';
import {initialDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
import {createWorkspacePlan} from '../../web-frontend/src/services/workspace-plan.js';
import {nativeDatasetIdentity} from '../../web-frontend/src/services/native-dataset.js';

const storage=new Map();
globalThis.localStorage={getItem:k=>storage.get(k)||null,setItem:(k,v)=>storage.set(k,v),removeItem:k=>storage.delete(k)};
globalThis.confirm=()=>true;
globalThis.window={removeEventListener(){}};
globalThis.document={querySelector:()=>null,activeElement:null,removeEventListener(){}};
const newStore=()=>{setActivePinia(createPinia());return useChatStore();};
const plan=initialDemoPlan();
const orderA={orderId:plan.orderIds[0],product:'A',quantity:1,taskTag:'F',planStart:'2026-10-19',planEnd:'2026-10-20',dueDate:'2026-10-22'};
const orderB={...orderA,orderId:plan.orderIds[1],product:'B'};

test('filtering and paging clear a hidden order selection and use only visible gantt rows',async t=>{
 const spec={native:true,view:'overview',baseState:plan,baseRevision:0,orders:[orderA,orderB],allOrders:[orderA,orderB],chart:{rows:[{orderId:orderA.orderId},{orderId:orderB.orderId}],items:[{rowIndex:0},{rowIndex:1}]}};
 const card=await setupSfc('web-frontend/src/components/NativeDemoCard.vue',{block:{spec},currentPlan:plan,conversationId:'filter'});t.after(()=>card.unmount());
 const table=await setupSfc('web-frontend/src/components/PagedDataTable.vue',{rows:[orderA,orderB],columns:card.state.orderColumns.value,initialQuery:'',filters:[],compact:false});t.after(()=>table.unmount());
 card.state.selectOrder(orderA);
 table.state.query.value=orderB.orderId;await vue.nextTick();
 const filtered=table.state.result.value.rows;
 // Follow the actual page-change event handler used by NativeDemoCard.
 const handler=card.state.updatePageRows||((rows)=>card.state.pageRows.value=rows);
 handler(filtered);await vue.nextTick();
 assert.equal(card.state.selectedOrderId.value,'');
 assert.deepEqual(card.state.chartBlock.value.spec.chart.rows.map(o=>o.orderId),[orderB.orderId]);
 card.state.selectOrder(orderB);
 table.state.query.value='NO-SUCH-ORDER';await vue.nextTick();handler(table.state.result.value.rows);await vue.nextTick();
 assert.equal(card.state.selectedOrderId.value,'');assert.deepEqual(card.state.chartBlock.value.spec.chart.rows,[]);
});

test('viewing history and saving card UI do not change message recency',async()=>{
 const store=newStore();
 store.current.messages.push({role:'user',content:'旧对话'});await store.persist();const old=store.current.id;
 await store.create();store.current.messages.push({role:'user',content:'新对话'});await store.persist();const latest=store.current.id;
 const before=store.conversations.map(c=>[c.id,c.updatedAt]);
 await store.select(old);await store.select(latest);
 assert.deepEqual(store.conversations.map(c=>[c.id,c.updatedAt]),before);
 await store.select(old);
 const block={uiState:{operationId:'history-ui',pending:false}};
 store.current.messages.push({role:'assistant',content:'结果',uiBlocks:[block]});await store.persist();
 const updated=store.conversations.map(c=>[c.id,c.updatedAt]);
 await store.persistBlockUi(old,block);
 assert.deepEqual(store.conversations.map(c=>[c.id,c.updatedAt]),updated);
});

test('deleting a conversation cancels its detail operation and rejects a late release',async t=>{
 const store=newStore();store.current.messages.push({role:'user',content:'下发'});await store.persist();const owner=store.current.id;
 let saved=structuredClone(plan);
 const workspace=createWorkspacePlan({async read(){return{plan:saved};},async transact(fn){const next=fn({plan:saved});saved=next.plan;return next;}});
 const planStore={current:plan,ready:true,async refresh(){return this.current=await workspace.read();},async apply(spec,options){return this.current=await workspace.apply(spec,options);}};
 const app=await setupSfc('web-frontend/src/App.vue',{}, {useChatStore:()=>store,usePlanStore:()=>planStore,createVoiceCapture:()=>({cancel(){}}),stopVolcTts:()=>{}});t.after(()=>app.unmount());
 const spec={native:true,view:'release',prepare:true,demandVersion:plan.demandVersion,baseRevision:0,baseState:plan,orderId:'',orders:[orderA,orderB],allOrders:[orderA,orderB]};
 const block=vue.reactive({type:'interactive-demo',spec});store.current.messages.push({role:'assistant',content:'确认',uiBlocks:[block]});
 const props={block,currentPlan:plan,conversationId:owner,persistUi:app.state.persistPresentation,resolvePlan:app.state.resolveCurrentPlan,acceptResult:app.state.appendDemoResult};
 const compact=await setupSfc('web-frontend/src/components/NativeDemoCard.vue',{...props,compact:true});t.after(()=>compact.unmount());
 const detail=await setupSfc('web-frontend/src/components/NativeDemoCard.vue',{...props,compact:false});t.after(()=>detail.unmount());
 app.state.viewer.value={type:'interactive-demo',id:'release',block};
 let respond,signal;const originalFetch=globalThis.fetch;t.after(()=>globalThis.fetch=originalFetch);
 globalThis.fetch=async(url,options)=>{if(url==='/api/agent/health')return new Response(JSON.stringify({status:'ok',mode:'model',dataset:nativeDatasetIdentity}));signal=options.signal;return await new Promise(resolve=>respond=resolve);};
 const waiting=detail.state.calculate();for(let n=0;n<20&&!respond;n++)await Promise.resolve();assert.equal(typeof respond,'function');
 await app.state.removeConversation(store.current);
 compact.unmount();
 const released={...structuredClone(plan),revision:1,releaseStatus:'released',nativeReleaseId:'e'.repeat(32),releasedCalculationId:plan.nativeResultId};
 const receipt={...spec,prepare:false,receipt:{sourceResultId:plan.nativeResultId},proposedPlan:released};
 const events=[['delta',{text:'已下发'}],['ui_payload',{blocks:[{type:'interactive-demo',spec:receipt}]}],['done',{}]].map(([event,data])=>`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`).join('');
 respond(new Response(events));await waiting;
 assert.equal(signal.aborted,true);assert.equal(app.state.viewer.value,null);
 assert.equal(saved.releaseStatus,'unreleased');
 await assert.rejects(()=>app.state.appendDemoResult({originConversationId:owner,summary:'迟到',uiPayload:[{type:'interactive-demo',spec:receipt}]}),/取消|删除|离开/);
 assert.equal(saved.releaseStatus,'unreleased');
});

test('a notification for an already committed mutation stays with its owner after navigation',async t=>{
 const store=newStore();store.current.messages.push({role:'user',content:'采用'});await store.persist();const owner=store.current.id;
 await store.create();const current=store.current.id;
 const app=await setupSfc('web-frontend/src/App.vue',{}, {useChatStore:()=>store,usePlanStore:()=>({}),createVoiceCapture:()=>({cancel(){}}),stopVolcTts:()=>{}});t.after(()=>app.unmount());
 await app.state.appendDemoResult({originConversationId:owner,summary:'方案已采用',uiPayload:[]});
 assert.equal(store.current.id,current);assert.equal(store.current.messages.length,0);
 assert.equal(store.findConversation(owner).messages.at(-1).content,'方案已采用');
});
