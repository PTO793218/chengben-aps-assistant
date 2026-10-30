import test from 'node:test';
import assert from 'node:assert/strict';
import largeBaseline from '../../agent-backend/Data/datasets/large85/native-baseline.json' with {type:'json'};
import {businessMetrics} from '../../web-frontend/src/services/business-presentation.js';

test('dispatch counts only orders actually linked to its visible tasks',()=>{
 const spec={view:'dispatch',orders:[{orderId:'A'},{orderId:'B'},{orderId:'C'}],tasks:[{state_type:'run',orderIds:['A']},{state_type:'run',orderIds:['A']}]};
 assert.equal(businessMetrics(spec).find(m=>m.label==='关联订单').value,1);
 assert.equal(businessMetrics({...spec,orders:[{orderId:'B'}],tasks:[]}).find(m=>m.label==='关联订单').value,0);
});

test('dispatch ignores outside-scope task links and deduplicates merged tasks',()=>{
 const spec={view:'dispatch',orders:[{orderId:'A'},{orderId:'B'},{orderId:'C'}],tasks:[{state_type:'run',orderIds:['A','B','outside']},{state_type:'run',orderIds:['B']}]};
 assert.equal(businessMetrics(spec).find(m=>m.label==='关联订单').value,2);
});

test('the verified 85-order dispatch payload reports one linked order and zero for the new order',()=>{
 const orders=largeBaseline.orders.map(o=>({orderId:o.sale_order_no}));
 const tasks=largeBaseline.tasks.map(t=>({...t,orderIds:largeBaseline.orders.filter(o=>t.sale_order_guid.split(',').includes(o.sale_order_guid)).map(o=>o.sale_order_no)})).filter(t=>t.orderIds.length);
 const baseline={view:'dispatch',orders,tasks};assert.equal(baseline.orders.length,84);assert.equal(baseline.tasks.length,6);
 assert.equal(businessMetrics(baseline).find(m=>m.label==='关联订单').value,1);
 assert.equal(businessMetrics({view:'dispatch',orders:[{orderId:'Z99200020'}],tasks:[]}).find(m=>m.label==='关联订单').value,0);
});
