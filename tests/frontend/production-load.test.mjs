import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {makeProductionScene} from '../../web-frontend/src/services/production.js';
import {calculateLoad,taskDetails} from '../../web-frontend/src/services/production-details.js';
import {calendarUnknownHoursColors,calendarColors} from '../../web-frontend/src/services/production.js';
const data=JSON.parse(fs.readFileSync(new URL('../../agent-backend/Data/production-demo.json',import.meta.url)));

test('legacy snapshots retain visible planned days and their exact tasks without fabricated load',()=>{
 const legacy=structuredClone(data);delete legacy.capacity;
 for(const task of legacy.tasks)delete task.plannedHours;
 const scene=makeProductionScene(legacy,'calendar');
 const planned=scene.cells.filter(c=>c.count>0);
 assert.equal(planned.length,52);
 assert.equal(planned.reduce((sum,c)=>sum+c.count,0),104);
 assert.ok(planned.every(c=>c.color===calendarUnknownHoursColors.planned && c.load.ratio===null));
 assert.ok(scene.cells.filter(c=>c.state==='unknown').every(c=>c.color===calendarColors.unknown));
 const current=makeProductionScene(data,'calendar');
 for(const cell of planned)assert.deepEqual(cell.taskIds,current.cells.find(c=>c.id===cell.id).taskIds);
});

test('partially missing hours only changes the affected day color, preserving other loads',()=>{
 const partial=structuredClone(data);delete partial.tasks[0].plannedHours;
 const scene=makeProductionScene(partial,'calendar');
 assert.equal(scene.cells.find(c=>c.id==='turn-1/2026-09-07').color,calendarUnknownHoursColors.planned);
 assert.ok(scene.cells.some(c=>c.count>0&&c.load.ratio!==null&&c.color!==calendarUnknownHoursColors.planned));
});

test('every line/day reconciles task IDs and planned hours across both views, with filters',()=>{
 for(const filter of [{},{orderId:'Z9900001'},{lineId:'turn-1'},{scope:'risk'},{stage:'磨工'},{scope:'pending'}]){
  const schedule=makeProductionScene(data,'schedule',filter,{start:data.coverageStart,end:data.coverageEnd});
  const calendar=makeProductionScene(data,'calendar',filter);
  for(const day of calendar.cells){
   const cells=schedule.cells.filter(c=>c.task.lineId===day.lineId&&c.task.date===day.date);
   assert.deepEqual(day.taskIds.slice().sort(),cells.map(c=>c.id).sort());
   if(day.date>=data.coverageStart&&day.date<=data.coverageEnd){
    const hours=cells.reduce((sum,c)=>sum+c.task.plannedHours,0);
    assert.equal(day.load.plannedHours,hours);
    if(day.load.capacityHours>0)assert.equal(day.load.ratio,hours/day.load.capacityHours);
   }
  }
 }
});

test('load distinguishes missing hours, zero capacity and overload without clamping',()=>{
 assert.equal(calculateLoad({knownHours:18,missingHours:0},15,true).ratio,1.2);
 assert.equal(calculateLoad({knownHours:5,missingHours:1},15,true).ratio,null);
 assert.equal(calculateLoad({knownHours:5},undefined,true).status,'unknown');
 assert.equal(calculateLoad({knownHours:5},0,true).status,'zero-capacity');
 assert.equal(calculateLoad(undefined,0,true).status,'off');
 assert.equal(calculateLoad(undefined,15,false).ratio,null);
});

test('hover uses component and material fields without substituting the finished product',()=>{
 const task={...data.tasks[0],componentName:'试验部件',materialName:'试验物料',materialCode:'M-001',materialSpec:'12×30'};
 const rows=Object.fromEntries(taskDetails(data,task));
 assert.equal(rows['部件品名'],'试验部件');assert.equal(rows['物料编码'],'M-001');
 assert.equal(Object.fromEntries(taskDetails(data,data.tasks[0]))['部件品名'],'未提供');
});

test('larger spacing separates coordinates while retaining bar size and all date anchors',()=>{
 const a=makeProductionScene(data,'schedule',{}, {spacing:1,dateSpacing:1,lineSpacing:1});
 const b=makeProductionScene(data,'schedule',{}, {spacing:2,dateSpacing:2,lineSpacing:2});
 assert.equal(a.cells.length,b.cells.length);
 assert.equal(b.cells[1].x,2*a.cells[1].x);assert.equal(a.cells[1].w,b.cells[1].w);
 assert.equal(a.labels.filter(l=>l.axis==='x').length,28);
});
