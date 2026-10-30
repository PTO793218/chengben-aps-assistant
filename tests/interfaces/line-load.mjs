import assert from 'node:assert/strict';
import fs from 'node:fs';
const baseline=JSON.parse(fs.readFileSync(new URL('../../temp/business-data/line-load-reference.json',import.meta.url),'utf8'));
const result=await fetch((process.env.BASE_URL||'http://localhost:19440')+'/api/agent/tools/aps-analysis?type=order&source=business&orderId=Z9900027&version=260904001').then(r=>r.json());
const load=result.uiPayload[0].spec.lineLoad;
const close=(actual,expected)=>assert.ok(Math.abs(actual-expected)<1e-7,`${actual} != ${expected}`);
assert.equal(load.available,true);
for(const reference of baseline[0]) {
 const row=load.periods.find(r=>r.grain==='month'&&r.periodKey==='2026-09'&&r.lineId===reference.lineId);
 assert.ok(row);assert.equal(reference.calendarDays,30);
 for(const field of ['capacityHours','madeHours','changeHours','orderMadeHours','orderChangeHours','recordCount'])close(row[field],reference[field]);
 close(row.plannedHours,reference.madeHours+reference.changeHours);
 close(row.loadPercent,(reference.madeHours+reference.changeHours)/reference.capacityHours*100);
}
for(const [line,cap] of [['cxcx2',0],['S09-3',8],['youlu',15.5]]){
 const day=load.periods.find(r=>r.grain==='day'&&r.periodKey==='2026-09-20'&&r.lineId===line);
 close(day.capacityHours,cap);
}
const missing=load.periods.find(r=>r.grain==='month'&&r.periodKey==='2026-10'&&r.lineId==='cxcx2');
assert.equal(missing.capacityHours,null);assert.equal(missing.loadPercent,null);assert.equal(missing.missingCalendarDays,31);
assert.ok(!JSON.stringify(result.agentContext).includes('periods'));
fs.writeFileSync(new URL('../../temp/verification/line-load-api.json',import.meta.url),JSON.stringify(result,null,2));
console.log('PASS: line load agrees with independent SQL for all 3 lines; September totals, 09-20 nonworking/8/15.5h, missing October calendar, no detail in model context.');
