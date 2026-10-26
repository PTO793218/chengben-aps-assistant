import test from 'node:test';
import assert from 'node:assert/strict';
import {initialDemoPlan} from '../../web-frontend/src/services/demo-plan.js';
import {operationState} from '../../web-frontend/src/services/business-presentation.js';
test('native cards compare full saved MRP and release state, not only plan id and revision',()=>{
 const p=initialDemoPlan(),spec={native:true,prepare:true,view:'reschedule',baseRevision:p.revision,baseState:p};
 assert.equal(operationState(spec,p).canExecute,true);
 assert.equal(operationState(spec,{...p,nativeMrpResultId:'a'.repeat(32)}).canExecute,false);
 assert.equal(operationState(spec,{...p,orderIds:[...p.orderIds].reverse()}).canExecute,false);
 assert.equal(operationState(spec,{}).canExecute,false);
});
test('old receipt is historical and cannot claim the current plan is released',()=>{
 const p=initialDemoPlan(),spec={native:true,view:'release',receipt:{sourceResultId:p.nativeResultId},proposedPlan:{...p,releaseStatus:'released',nativeReleaseId:'a'.repeat(32)}};
 assert.equal(operationState(spec,p).key,'superseded');
 assert.equal(operationState(spec,spec.proposedPlan).key,'released');
});
test('native read results indicate that newer adopted plan or MRP exists',()=>{
 const p=initialDemoPlan(),spec={native:true,view:'overview',baseState:p,baseRevision:p.revision};
 assert.equal(operationState(spec,p).key,'view');
 assert.equal(operationState(spec,{...p,revision:1}).key,'snapshot');
});
