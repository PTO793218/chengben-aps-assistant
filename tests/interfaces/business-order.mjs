import assert from 'node:assert/strict';
import fs from 'node:fs';
const base=process.env.BASE_URL||'http://localhost:18440';
const reference=JSON.parse(fs.readFileSync(new URL('../../temp/business-data/order-reference.json',import.meta.url),'utf8'));
const response=await fetch(base+'/api/agent/tools/aps-analysis?type=order&source=business&orderId='+reference.orderId+'&version='+reference.version);
assert.equal(response.status,200);
const result=await response.json();
assert.equal(result.agentContext.source,'business-snapshot');
const spec=result.uiPayload[0].spec;
const originals=reference.tables[1];
assert.equal(spec.lineLoad.available,true);
assert.equal(spec.arrangement,undefined);
assert.equal(spec.chart.items.length,originals.length);
assert.equal(spec.detail.rows.length,originals.length);
for(const original of originals){
  const row=spec.detail.rows.find(x=>x.id===original.plan_guid);
  assert.ok(row,'No original plan may be omitted');
  for(const [actual,expected] of [['calcGuid','calc_guid'],['productNo','product_no'],['lineId','product_line_no'],['date','start_date'],['quantity','plan_num'],['plannedHours','made_time'],['changeHours','change_time']])assert.equal(row[actual],original[expected]);
  assert.equal(row.shift,original.shift_no==='day'?'白班':'晚班');
}
for(const original of reference.tables[0]){
  for(let i=1;i<=reference.tables[2].length;i++){
    const raw=original['plan_num'+i];if(!raw)continue;
    const date=reference.tables[2][i-1];
    const items=spec.chart.items.filter(x=>x.columnIndex===i-1&&x.lineId===original.product_line_no&&spec.chart.rows[x.rowIndex].productSpec===original.material_no);
    assert.equal(items.reduce((n,x)=>n+x.quantity,0),Number(raw),'Original matrix cell must match');
    assert.ok(items.every(x=>x.date===date.delivery_date));
  }
}
assert.equal((await fetch(base+'/api/agent/tools/aps-analysis?type=order&source=business&orderId=DOESNOTEXIST')).status,400);
assert.equal((await fetch(base+'/api/agent/tools/aps-analysis?type=order&source=business&orderId='+reference.orderId+'&version=missing')).status,400);
assert.equal((await fetch(base+'/api/agent/tools/aps-analysis?type=overview&source=business')).status,400);
assert.ok(!JSON.stringify(result.agentContext).includes('plan_guid'));
fs.mkdirSync(new URL('../../temp/verification/',import.meta.url),{recursive:true});
fs.writeFileSync(new URL('../../temp/verification/business-order-api.json',import.meta.url),JSON.stringify(result,null,2));
console.log(`PASS ${reference.orderId} version=${reference.version}: ${originals.length} original plan records and all nonempty original matrix cells match; unknown order/version refused.`);
