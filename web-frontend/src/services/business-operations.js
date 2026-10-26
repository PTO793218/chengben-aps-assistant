import {reactive,toRaw} from 'vue';
import {createId} from './id.js';
const operations=new WeakMap();
const owners=new Map();
const pending=new Set();
export function claimOperation(key){
 if(pending.has(key))throw Error('相同操作正在处理中，请等待结果。');
 pending.add(key);return ()=>pending.delete(key);
}
export function cancelConversationOperations(owner){
 for(const entry of owners.get(owner)||[]){
  if(!entry.state.controller)continue;
  entry.block.uiState={...entry.block.uiState,cancelled:true,pending:false};
  entry.state.controller?.abort('user');
 }
}
export function acquireOperation(block,owner){
 const key=toRaw(block);let entry=operations.get(key);
 if(!entry){
  block.uiState={...block.uiState,operationId:block.uiState?.operationId||createId()};
  const interrupted=block.uiState?.pending===true;
  if(interrupted)block.uiState={...block.uiState,pending:false,cancelled:true};
  entry={users:0,block,owner,state:reactive({orderId:block.spec.orderId||'',position:block.spec.position||1,loading:false,controller:null,status:interrupted?'上次等待已中断，当前方案保留。请重新准备。':'',error:''})};
  operations.set(key,entry);
  if(owner){if(!owners.has(owner))owners.set(owner,new Set());owners.get(owner).add(entry);}
 }
 entry.users++;let released=false;
 return {state:entry.state,release(){if(released)return;released=true;if(--entry.users===0){entry.state.controller?.abort('user');operations.delete(key);const entries=owners.get(entry.owner);entries?.delete(entry);if(entries?.size===0)owners.delete(entry.owner);}}};
}
