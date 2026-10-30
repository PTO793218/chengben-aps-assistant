import productionDemo from '../../../agent-backend/Data/production-demo.json' with {type:'json'};
import {nativeDemo} from './native-dataset.js';
const lockedIds=productionDemo.orders.filter(o=>o.productionStatus==='inProgress').map(o=>o.id);
const pendingIds=productionDemo.orders.filter(o=>o.productionStatus==='notStarted').map(o=>o.id);
const ids=[...lockedIds,...pendingIds];
export const DEMAND_VERSION=nativeDemo.demandVersion, RELEASED_VERSION='261015002';
export const initialLegacyDemoPlan=()=>({schemaVersion:3,demandVersion:'261015001',revision:0,orderIds:[...ids],demandQuantities:{},supplyScenario:'stock',releaseStatus:'unreleased',releasedCalculationId:'',mrpInputs:[]});
export const initialDemoPlan=()=>({schemaVersion:4,demandVersion:DEMAND_VERSION,revision:0,orderIds:[...nativeDemo.baselineOrderIds],demandQuantities:{},supplyScenario:'frozen',releaseStatus:'unreleased',releasedCalculationId:'',mrpInputs:[],nativeResultId:nativeDemo.baselineResultId,nativeMrpResultId:nativeDemo.baselineMrpResultId,nativeReleaseId:''});
const clone=value=>JSON.parse(JSON.stringify(value));
const valid=p=>p?.schemaVersion===3&&p.demandVersion==='261015001'&&Number.isInteger(p.revision)&&p.revision>=0&&p.revision<2147483646&&Array.isArray(p.orderIds)&&p.orderIds.length===ids.length&&new Set(p.orderIds).size===ids.length&&p.orderIds.every(id=>ids.includes(id))&&p.orderIds.slice(0,lockedIds.length).every((id,i)=>id===lockedIds[i])&&p.demandQuantities&&typeof p.demandQuantities==='object'&&!Array.isArray(p.demandQuantities)&&Object.entries(p.demandQuantities).every(([id,q])=>pendingIds.includes(id)&&Number.isInteger(q)&&q>=1&&q<=100000)&&['none','stock','supply','late'].includes(p.supplyScenario)&&['unreleased','released'].includes(p.releaseStatus)&&(!p.mrpInputs||p.mrpInputs.length===0);
const key=p=>JSON.stringify([p?.schemaVersion,p?.demandVersion,p?.revision,p?.orderIds,ids.map(id=>p?.demandQuantities?.[id]??productionDemo.orders.find(o=>o.id===id).quantity),p?.supplyScenario,p?.releaseStatus,p?.releasedCalculationId||'']);
export function adoptDemoPlan(current,candidate){
 if(current?.schemaVersion===4)return adoptNativePlan(current,candidate);
 if(!valid(current)||current.releaseStatus==='released')throw new Error('模拟版本已锁定或属于旧版，请新建对话。');
 if(!valid(candidate?.proposedPlan)||candidate.demandVersion!=='261015001'||candidate.baseRevision!==current.revision||candidate.proposedPlan.revision!==current.revision+1||key(candidate.baseState)!==key(current))throw new Error('当前版本需求、供应或顺序已变化，请按最新方案重新计算；未重复采用。');
 if(candidate.proposedPlan.releaseStatus==='released'&&(candidate.view!=='release'||!candidate.calculationId||candidate.proposedPlan.releasedCalculationId!==candidate.calculationId))throw new Error('缺少有效模拟下达确认，未生成任务。');
 return clone(candidate.proposedPlan);
}
export function resetDemoPlan(current){
 if(current?.schemaVersion===4){if(!validNative(current)||current.releaseStatus==='released')throw new Error('当前计划已下发或状态有误，不能恢复。');return {...clone(current),revision:current.revision+1,orderIds:[...nativeDemo.baselineOrderIds],nativeResultId:nativeDemo.baselineResultId};}
 if(!valid(current)||current.releaseStatus==='released')throw new Error('模拟版本已下达锁定或属于旧版，不能恢复，请新建对话。');
 return {...clone(current),revision:current.revision+1,orderIds:[...ids]};
}
export function saveDemoMrp(current,result){
 if(current?.schemaVersion===4){if(result?.view!=='mrp')throw new Error('未取得有效的物料核算结果');return adoptNativePlan(current,result);}
 if(result?.view!=='mrp'||result.after?.orders?.find(o=>o.orderId===result.orderId)?.quantity!==result.quantity||result.proposedPlan?.supplyScenario!==result.scenario)throw new Error('MRP整批结果输入无效，未保存。');
 return adoptDemoPlan(current,result);
}
export async function requestDemo(body, signal) {
  const {plan,conversationId,...demoAction}=body;
  const response = await fetch('/api/agent/chat/stream', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({conversationId,message:`请按已确认的信息执行${body.action==='mrp'?'MRP计算':body.action==='release'?'下发':'整批试排'}，完成后告诉我结果。`,history:[],demoPlan:plan,demoAction}), signal });
  if(!response.ok)throw new Error(`请求失败（HTTP ${response.status}），当前方案保留。`);
  let text='',blocks=[],done;
  await readSse(response.body,(event,data)=>{
    if(event==='error')throw new Error(data.message||'本次处理未完成，当前方案保留，请重试。');
    if(event==='delta')text+=data.text||'';
    if(event==='ui_payload')blocks.push(...(data.blocks||[]));
    if(event==='done')done=data;
  });
  const matching=blocks.filter(b=>b.type==='interactive-demo'&&b.spec?.view===body.action&&b.spec?.orderId===body.orderId);
  if(!done||matching.length!==1)throw new Error('未取得已确认订单的有效结果，当前方案保留，请重试。');
  const spec=matching[0].spec;
  if(body.action==='mrp'&&!spec.native&&(spec.quantity!==body.quantity||spec.scenario!==body.scenario))throw new Error('返回结果与已确认需求不一致，未采用该结果。');
  const execution={modelInvoked:done.modelInvoked===true,mode:done.mode||done.configuredMode,toolCalls:done.toolCalls||[],requestId:done.requestId};
  return {success:true,summary:text,uiPayload:matching.map(block=>({...block,execution})),execution};
}
import { readSse } from './sse.js';

const validToken=v=>typeof v==='string'&&/^[a-f0-9]{32}$/.test(v);
const validNative=p=>p?.schemaVersion===4&&p.demandVersion===DEMAND_VERSION&&Number.isInteger(p.revision)&&p.revision>=0&&p.revision<2147483646&&Array.isArray(p.orderIds)&&new Set(p.orderIds).size===p.orderIds.length&&nativeDemo.baselineOrderIds.every(id=>p.orderIds.includes(id))&&p.orderIds.every(id=>[...nativeDemo.baselineOrderIds,...(nativeDemo.insertionOrders||[]).map(o=>o.orderId)].includes(id))&&p.supplyScenario==='frozen'&&p.demandQuantities&&Object.keys(p.demandQuantities).length===0&&(!p.mrpInputs||p.mrpInputs.length===0)&&validToken(p.nativeResultId)&&validToken(p.nativeMrpResultId)&&['unreleased','released'].includes(p.releaseStatus)&&(p.releaseStatus==='released'?validToken(p.nativeReleaseId)&&p.releasedCalculationId===p.nativeResultId:p.nativeReleaseId===''&&p.releasedCalculationId==='');
const nativeKey=p=>JSON.stringify([p.schemaVersion,p.demandVersion,p.revision,p.orderIds,p.nativeResultId,p.nativeMrpResultId,p.nativeReleaseId,p.releaseStatus,p.releasedCalculationId,p.supplyScenario,p.demandQuantities]);
export const isCurrentNativePlan=validNative;
export const nativePlanKey=p=>p?nativeKey(p):'';
export const samePlanBase=(a,b)=>!!a&&!!b&&nativeKey(a)===nativeKey(b);
function adoptNativePlan(current,c){
 if(!validNative(current)||current.releaseStatus==='released')throw new Error('当前计划已下发或状态有误。');
 if(!c?.native||!validNative(c.proposedPlan)||c.demandVersion!==DEMAND_VERSION||!c.baseState||nativeKey(c.baseState)!==nativeKey(current)||c.baseRevision!==current.revision||c.proposedPlan.revision!==current.revision+1)throw new Error('基准方案已变化或结果过期，未重复采用。');
 const next=c.proposedPlan;
 if(['reschedule','insert','release'].includes(c.view)&&next.nativeMrpResultId!==current.nativeMrpResultId)throw new Error('计划调整不能改变已保存的物料核算引用。');
 if(['reschedule','insert'].includes(c.view)&&(next.releaseStatus!==current.releaseStatus||next.nativeReleaseId!==current.nativeReleaseId||next.releasedCalculationId!==current.releasedCalculationId))throw new Error('试排不能改变计划下发状态。');
 if(['reschedule','insert'].includes(c.view)&&next.nativeResultId!==c.calculationId)throw new Error('采用的计算结果与已展示结果不一致。');
 if(c.view==='mrp'&&(next.nativeResultId!==current.nativeResultId||JSON.stringify(next.orderIds)!==JSON.stringify(current.orderIds)||next.releaseStatus!==current.releaseStatus||next.nativeReleaseId!==current.nativeReleaseId||next.releasedCalculationId!==current.releasedCalculationId||next.nativeMrpResultId!==c.mrpResultId))throw new Error('物料核算不能改变生产计划或订单顺序。');
 if(c.view==='release'&&(next.releaseStatus!=='released'||!validToken(next.nativeReleaseId)||next.nativeResultId!==current.nativeResultId||c.receipt?.sourceResultId!==current.nativeResultId||next.releasedCalculationId!==current.nativeResultId))throw new Error('下发与当前已展示计划不一致，未执行。');
 if(c.view==='release'&&JSON.stringify(next.orderIds)!==JSON.stringify(current.orderIds))throw new Error('下发不能新增、遗漏订单或修改顺序。');
 if(c.view==='insert'&&(current.orderIds.includes(c.orderId)||next.orderIds.length!==current.orderIds.length+1||next.orderIds[0]!==c.orderId||!current.orderIds.every(id=>next.orderIds.includes(id))||next.nativeMrpResultId!==current.nativeMrpResultId))throw new Error('新增订单与当前计划范围不一致。');
 if(c.view==='reschedule'&&(next.orderIds.length!==current.orderIds.length||!current.orderIds.every(id=>next.orderIds.includes(id))))throw new Error('调序不能新增或删除订单。');
 if(!['reschedule','insert','mrp','release'].includes(c.view))throw new Error('该结果不能采用。');
 return clone(next);
}
