import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {calendarDays,selectProduction,makeProductionScene,summarize} from '../../web-frontend/src/services/production.js';
const data=JSON.parse(fs.readFileSync(new URL('../../agent-backend/Data/production-demo.json',import.meta.url)));
test('unfinished includes unscheduled orders and excludes completed orders',()=>{
 const selected=selectProduction(data),counts=summarize(selected);
 assert.equal(counts.total,12);assert.equal(counts.full,8);assert.equal(counts.partial,2);assert.equal(counts.none,2);
 assert.equal(counts.full+counts.partial+counts.none,counts.total);
 assert.equal(selected.pending.length,10);assert.ok(!selected.orders.some(o=>o.id==='Z9900004'));
 const pendingOrder=selectProduction(data,{orderId:'Z9900006'});
 assert.equal(pendingOrder.orders.length,1);assert.equal(pendingOrder.tasks.length,0);assert.equal(pendingOrder.pending.length,3);
});
test('calendar uses actual dates including leap days and Monday first',()=>{
 const days=calendarDays(2024);assert.equal(days.length,366);assert.equal(new Set(days.map(d=>d.date)).size,366);
 assert.deepEqual(days[0],{date:'2024-01-01',week:0,weekday:0});assert.ok(days.some(d=>d.date==='2024-02-29'));
 const y=calendarDays(2026);assert.equal(y.length,365);assert.equal(y[0].weekday,3);
});
test('calendar and schedule agree on task counts; unknown is not empty',()=>{
 const scene=makeProductionScene(data,'calendar');
 const day=scene.cells.find(c=>c.lineId==='turn-1'&&c.date==='2026-09-07');
 assert.equal(day.count,selectProduction(data).tasks.filter(t=>t.lineId===day.lineId&&t.date===day.date).length);
 assert.equal(scene.cells.find(c=>c.date==='2026-01-01').state,'unknown');
 assert.equal(scene.cells.find(c=>c.lineId==='turn-1'&&c.date==='2026-10-31').state,'empty');
 const orderScene=makeProductionScene(data,'schedule',{orderId:'Z9900001'},{start:'2026-09-01',end:'2026-10-31',granularity:'shift'});
 assert.equal(new Set(orderScene.cells.map(c=>c.y)).size,1);assert.equal(new Set(orderScene.cells.map(c=>c.task.lineId)).size,3);
 assert.ok(orderScene.cells.every(c=>c.kind==='task'));assert.equal(orderScene.cells.length,12);
});

test('every shift remains a separate bar in both preview and panorama',()=>{
 const scene=makeProductionScene(data,'schedule');
 const tasks=selectProduction(data).tasks.filter(t=>t.date>=data.defaultStart&&t.date<=data.defaultEnd);
 assert.deepEqual(scene.cells.map(c=>c.id).sort(),tasks.map(t=>t.id).sort());
 assert.ok(scene.cells.every(c=>c.kind==='task'));
 const day=scene.cells.filter(c=>c.task.orderId==='Z9900001'&&c.task.date==='2026-09-07');
 assert.equal(day.length,2);assert.ok(day[0].x<day[1].x);
});

test('daily window keeps other planned tasks outside without creating fake blocks',()=>{
 const scene=makeProductionScene(data,'schedule',{}, {start:'2026-09-07',end:'2026-09-07'});
 assert.ok(scene.cells.every(c=>c.task.date==='2026-09-07'));assert.ok(scene.selection.tasks.length>scene.cells.length);
 assert.equal(selectProduction(data,{orderId:'missing'}).orders.length,0);
});
