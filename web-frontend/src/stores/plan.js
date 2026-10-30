import {defineStore} from 'pinia';
import {createWorkspacePlan,browserPlanRepository} from '../services/workspace-plan.js';
import {DEMAND_VERSION} from '../services/demo-plan.js';
let workspace,channel;
const service=()=>workspace||=createWorkspacePlan(browserPlanRepository());
export const usePlanStore=defineStore('workspace-plan',{
 state:()=>({current:null,ready:false,error:''}),
 actions:{
  async init(conversations){try{this.current=await service().init(conversations);this.ready=true;this.error='';
   if(typeof BroadcastChannel!=='undefined'&&!channel){channel=new BroadcastChannel('chengben-aps-plan:'+DEMAND_VERSION);channel.onmessage=()=>this.refresh().catch(()=>{});}
  }catch(e){this.error=e.message;throw e;}},
  async refresh(){try{this.current=await service().read();this.error='';return this.current;}catch(e){this.error=e.message;throw e;}},
  async apply(spec,options){try{this.current=await service().apply(spec,options);this.error='';channel?.postMessage('changed');return this.current;}catch(e){await this.refresh().catch(()=>{});this.error=e.message;throw e;}},
  async undo(spec,options){try{this.current=await service().undo(spec,options);this.error='';channel?.postMessage('changed');return this.current;}catch(e){await this.refresh().catch(()=>{});this.error=e.message;throw e;}}
 }
});
