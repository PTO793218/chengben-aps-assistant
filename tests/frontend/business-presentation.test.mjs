import test from 'node:test';
import assert from 'node:assert/strict';
let b={};try{b=await import('../../web-frontend/src/services/business-presentation.js');}catch(e){if(e.code!=='ERR_MODULE_NOT_FOUND')throw e;}
const fn=name=>{assert.equal(typeof b[name],'function',name+' must exist');return b[name];};
const rows=Array.from({length:500},(_,i)=>({orderId:'Z'+String(i+1).padStart(7,'0'),product:i%2?'轴承':'滚子',taskTag:i%5===0?'T':'F',risk:i%3===0,planStart:'2026-10-16',planEnd:'2026-10-21',dueDate:'2026-10-17'}));
test('500 records are searched before pagination and page boundaries are clamped',()=>{
 const select=fn('selectPage');let p=select(rows,{page:25,pageSize:20,keys:['orderId','product']});assert.equal(p.rows.length,20);assert.equal(p.rows[0].orderId,'Z0000481');assert.equal(p.pages,25);
 p=select(rows,{query:'Z0000499',page:25,pageSize:20,keys:['orderId','product']});assert.equal(p.total,500);assert.equal(p.filteredTotal,1);assert.equal(p.page,1);assert.equal(p.rows[0].orderId,'Z0000499');
 p=select(rows,{query:'没有这单',page:25,pageSize:20});assert.equal(p.rows.length,0);assert.equal(p.page,1);assert.equal(p.pages,1);
});
test('filters preserve whole-result counts and at most twenty detail rows',()=>{
 const p=fn('selectPage')(rows,{page:1,pageSize:20,filters:{taskTag:'T'},keys:['orderId']});assert.equal(p.total,500);assert.equal(p.filteredTotal,100);assert.equal(p.rows.length,20);assert.ok(p.rows.every(x=>x.taskTag==='T'));
});
test('chat previews stay at five while keeping a specified target',()=>{
 const preview=fn('orderPreview')({orders:rows,orderId:'Z0000499',view:'reschedule'});assert.equal(preview.length,5);assert.equal(preview[0].orderId,'Z0000499');
});
test('material focus selects terminal material facts without summing repeated operations or units',()=>{
 const source=[{orderId:'Z1',material:'M1',unit:'KG',active:'A01',level:'001',remaining:100},{orderId:'Z1',material:'M1',unit:'KG',active:'A02',level:'001',remaining:100},{orderId:'Z1',material:'M2',unit:'KG',active:'',level:'001.001',remaining:35},{orderId:'Z1',material:'M3',unit:'粒',active:'',level:'001.001',remaining:400}];
 const focus=fn('materialFocusRows')(source);assert.equal(focus.length,2);assert.equal(focus.find(x=>x.unit==='KG').remaining,35);assert.equal(focus.find(x=>x.unit==='粒').remaining,400);
});
test('cancelled confirmation cannot execute and an adopted result is not called expired',()=>{
 const state=fn('operationState');const spec={prepare:true,view:'mrp',baseRevision:0,baseState:{nativeResultId:'a'}};const current={revision:0,nativeResultId:'a',releaseStatus:'unreleased'};
 assert.equal(state(spec,current,{cancelled:true}).canExecute,false);assert.equal(state(spec,current,{cancelled:true}).key,'cancelled');
 assert.equal(state(spec,current).canExecute,true);
 const result={view:'reschedule',baseRevision:0,calculationId:'b',proposedPlan:{},baseState:{nativeResultId:'a'}};
 assert.equal(state(result,{...current,revision:1,nativeResultId:'b'}).key,'adopted');
 assert.equal(state(result,{...current,revision:2,nativeResultId:'c'}).key,'superseded');
 assert.equal(state(spec,{...current,releaseStatus:'released'}).canExecute,false);
});
test('MRP saved state follows its own result without changing plan identity',()=>{
 const spec={view:'mrp',baseRevision:0,mrpResultId:'m2',proposedPlan:{},baseState:{nativeResultId:'p',nativeMrpResultId:'m1'}};
 const s=fn('operationState')(spec,{revision:1,nativeResultId:'p',nativeMrpResultId:'m2',releaseStatus:'unreleased'});assert.equal(s.key,'saved');assert.equal(s.canExecute,false);
});
test('related actions are limited to two and preserve the selected order',()=>{
 const actions=fn('relatedActions')({native:true,view:'material',focusOrderId:'Z0000499',orders:rows});assert.ok(actions.length<=2);assert.ok(actions.every(a=>a.question.includes('Z0000499')));assert.ok(actions.every(a=>!/演示|GUID|本地|隔离/.test(a.question)));
});
test('legacy native message display is business-readable without changing stored evidence',()=>{
 const message={role:'assistant',content:'本地隔离演示。GUID '+ 'a'.repeat(32)+'。只使用本次摘要给出的日期。',uiBlocks:[{spec:{native:true,view:'overview',orders:rows.slice(0,12)}}]};const stored=message.content;
 const visible=fn('visibleAssistantText')(message);assert.ok(!/GUID|隔离|只使用|[a-f0-9]{32}/.test(visible));assert.equal(message.content,stored);assert.ok(visible.includes('12'));
});
test('metrics answer the current question rather than reusing plan metrics everywhere',()=>{
 const spec={view:'dispatch',orders:rows.slice(0,1),tasks:[{state_type:'run'},{state_type:'run'}]};const metrics=fn('businessMetrics')(spec);assert.ok(metrics.some(m=>m.label==='关联任务'&&m.value===2));assert.ok(!metrics.some(m=>m.label.includes('交期')));
});
test('completed preparation cannot run twice or be mistaken for a cancellation',()=>{
 const s=fn('operationState')({prepare:true,view:'mrp'},{},{completed:true});
 assert.equal(s.canExecute,false);assert.equal(s.key,'completed');assert.ok(s.label.includes('已完成'));
});
test('unknown task states stay unknown and chart rows rebase after paging',()=>{
 assert.equal(fn('statusName')('finish'),'已完成');assert.equal(fn('statusName')('mystery'),'状态待核对');
 const chart={rows:rows.slice(0,40),items:[{rowIndex:0,columnIndex:0},{rowIndex:20,columnIndex:2},{rowIndex:21,columnIndex:3}]};
 const scoped=fn('scopedChart')(chart,rows.slice(20,40).map(x=>x.orderId));assert.equal(scoped.rows.length,20);assert.deepEqual(scoped.items.map(x=>x.rowIndex),[0,1]);assert.equal(chart.items[1].rowIndex,20);
});
test('old schema3 records use a read-only business projection without rewriting original data',()=>{
 const block={title:'LOCAL SIMULATION',type:'interactive-demo',spec:{view:'reschedule',orders:rows,prepare:true,baseState:{revision:0},proposedPlan:{revision:1},calculationId:'a'.repeat(32)}},raw=JSON.stringify(block);
 const display=fn('historicalBlock')(block);assert.equal(display.spec.archived,true);assert.equal(display.spec.prepare,false);assert.equal(display.spec.proposedPlan,undefined);assert.equal(fn('operationState')(display.spec,{}).canExecute,false);assert.equal(display.spec.orders.length,500);assert.equal(JSON.stringify(block),raw);
});
