import test from 'node:test';
import assert from 'node:assert/strict';
let acquire;try{({acquireOperation:acquire}=await import('../../web-frontend/src/services/business-operations.js'));}catch(e){if(e.code!=='ERR_MODULE_NOT_FOUND')throw e;}
test('chat and detail share input, execution state and cancellation controller',()=>{
 assert.equal(typeof acquire,'function');const block={spec:{orderId:'Z9900010',position:3}},chat=acquire(block),detail=acquire(block);
 chat.state.orderId='Z9900006';chat.state.loading=true;chat.state.controller=new AbortController();
 assert.equal(detail.state.orderId,'Z9900006');assert.equal(detail.state.loading,true);detail.state.controller.abort('user');assert.equal(chat.state.controller.signal.aborted,true);
 chat.release();detail.release();
});
test('closing a detail does not abort chat work; leaving the conversation does',()=>{
 assert.equal(typeof acquire,'function');const block={spec:{}},chat=acquire(block),detail=acquire(block);chat.state.controller=new AbortController();detail.release();assert.equal(chat.state.controller.signal.aborted,false);chat.release();assert.equal(chat.state.controller.signal.aborted,true);
});
test('a refreshed pending confirmation requires explicit preparation again',()=>{
 assert.equal(typeof acquire,'function');const block={spec:{},uiState:{pending:true}},op=acquire(block);
 assert.equal(block.uiState.cancelled,true);assert.equal(block.uiState.pending,false);assert.equal(op.state.loading,false);assert.ok(op.state.status.includes('中断'));op.release();
});
test('identical confirmations in different cards cannot launch duplicate work',async()=>{
 const {claimOperation}=await import('../../web-frontend/src/services/business-operations.js');
 const release=claimOperation('same-plan-insert');
 assert.throws(()=>claimOperation('same-plan-insert'),/处理中/);
 const other=claimOperation('different-plan-insert');other();release();
 claimOperation('same-plan-insert')();
});

test('leaving an owner only cancels its running work, retaining completed cards and other owners',async()=>{
 const {cancelConversationOperations}=await import('../../web-frontend/src/services/business-operations.js');
 const completed={spec:{},uiState:{completed:true}},waiting={spec:{}},other={spec:{}};
 const done=acquire(completed,'leaving'),a=acquire(waiting,'leaving'),b=acquire(other,'remaining');
 a.state.controller=new AbortController();b.state.controller=new AbortController();
 try{
  cancelConversationOperations('leaving');
  assert.equal(a.state.controller.signal.aborted,true);
  assert.equal(b.state.controller.signal.aborted,false);
  assert.equal(completed.uiState.completed,true);
  assert.notEqual(completed.uiState.cancelled,true);
 }finally{done.release();a.release();b.release();}
});
