import test from 'node:test';
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
const require=createRequire(new URL('../../web-frontend/package.json',import.meta.url));
const {createPinia,setActivePinia}=await import(new URL('../../web-frontend/node_modules/pinia/dist/pinia.mjs',import.meta.url));
const {useChatStore}=await import('../../web-frontend/src/stores/chat.js');
const storage=new Map();globalThis.localStorage={getItem:k=>storage.get(k)||null,setItem:(k,v)=>storage.set(k,v),removeItem:k=>storage.delete(k)};
test('late results and UI state persist to their owner after switching, never to the active conversation',async()=>{
 setActivePinia(createPinia());const store=useChatStore();store.current.messages.push({role:'user',content:'原对话'});await store.persist();const owner=store.current.id;
 await store.create();store.current.messages.push({role:'user',content:'新对话'});await store.persist();const active=store.current.id;
 const block={uiState:{operationId:'owner-operation',pending:true},spec:{view:'reschedule'}};
 await store.appendResult(owner,{role:'assistant',content:'旧请求返回',uiBlocks:[block]});
 assert.equal(store.current.id,active);assert.equal(store.current.messages.length,1);
 block.uiState.pending=false;block.uiState.cancelled=true;await store.persistBlockUi(owner,block);
 await store.select(owner);assert.equal(store.current.messages.at(-1).content,'旧请求返回');assert.equal(store.current.messages.at(-1).uiBlocks[0].uiState.cancelled,true);
});
test('deleting the owner prevents a late request from resurrecting that conversation',async()=>{
 setActivePinia(createPinia());const store=useChatStore();store.current.messages.push({role:'user',content:'将删除'});await store.persist();const owner=store.current.id,old=store.current;
 await store.remove(owner);await store.appendResult(owner,{role:'assistant',content:'迟到结果'});await store.persist(old);
 assert.equal(store.conversations.some(c=>c.id===owner),false);assert.notEqual(store.current.id,owner);
});
