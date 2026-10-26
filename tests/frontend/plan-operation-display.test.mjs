import test from 'node:test';
import assert from 'node:assert/strict';
import * as presentation from '../../web-frontend/src/services/business-presentation.js';

const orders=[{orderId:'Z99100014',quantity:1050,position:1,planStart:'2026-10-20',planEnd:'2026-10-20',dueDate:'2026-10-23',risk:false,taskTag:'F'},{orderId:'Z9900026',position:2,planEnd:'2026-10-20',risk:false,taskTag:'T'}];
test('adding an order uses the same production plan title as a normal query',()=>{
 assert.equal(typeof presentation.businessTitle,'function');
 for(const view of ['insert','reschedule'])assert.equal(presentation.businessTitle({view,orders}),presentation.businessTitle({view:'overview',orders}));
 assert.equal(presentation.businessTitle({view:'insert',prepare:true}),'生产计划调整确认');
});
test('plan adjustment reuses plan metrics instead of special insertion counters',()=>{
 const spec={view:'insert',orders,beforeOrderCount:12,afterOrderCount:13,changes:[{added:true}]};
 assert.deepEqual(presentation.businessMetrics(spec),presentation.businessMetrics({...spec,view:'overview'}));
 assert.deepEqual(presentation.businessMetrics({...spec,view:'reschedule'}),presentation.businessMetrics({...spec,view:'overview'}));
});
test('an order number is sufficient for a standard insertion confirmation',()=>{
 const text=presentation.businessHeadline({view:'insert',prepare:true,orderId:'Z99100014',orders:[]});
 assert.match(text,/Z99100014/);assert.doesNotMatch(text,/急单|演示|场景/);
});
