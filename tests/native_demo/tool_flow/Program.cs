using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;

// Offline integration test: real business tools and isolated SQL worker, no model client.
var output=Environment.GetEnvironmentVariable("CONSISTENT_FLOW_OUTPUT")??"output/verification/consistent-data-20261019/tool-flow.json";
var p=new DemoPlanState{SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen",NativeResultId=NativeDemoGateway.Config.GetProperty("baselineResultId").GetString()!,NativeMrpResultId=NativeDemoGateway.Config.GetProperty("baselineMrpResultId").GetString()!};
var context=new ToolExecutionContext{DemoPlan=p};var tools=new InteractiveDemoTools(context);
var records=new List<object>();var checks=new List<string>();
void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks.Add(message);}
JsonElement Add(string label,ToolResult result)
{
    if(!result.Success)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{status="FAIL",label,summary=result.Summary,records,plan=p},new JsonSerializerOptions(NativeDemoGateway.Json){WriteIndented=true}));
    }
    Check(result.Success,label+"工具成功");
    records.Add(new{label,result});
    return JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec,NativeDemoGateway.Json);
}
DemoPlanState Adopt(JsonElement spec)=>JsonSerializer.Deserialize<DemoPlanState>(spec.GetProperty("proposedPlan"),NativeDemoGateway.Json)!;
ToolResult Act(string action,string id="",int position=1)=>tools.Execute(action,id,position,quantity:0,scenario:"frozen",plan:p,version:p.DemandVersion);
var baseline=Add("overview",NativeDemoWorkspace.Query(p,"overview"));
var originalPlan=p.NativeResultId;
var initialRisk=Add("risk-before",NativeDemoWorkspace.Query(p,"risk"));
Add("prepare-mrp",Act("prepare-mrp","Z9900006"));
var mrp=Add("mrp",Act("mrp","Z9900006"));p=Adopt(mrp);context.DemoPlan=p;
Check(p.NativeResultId==originalPlan,"保存独立MRP不更换生产计划");
Check(NativeDemoWorkspace.Valid(p),"MRP保存后仍属于当前数据集");
Add("material-before",new BusinessMaterialReadiness(context).Execute("order","Z9900006"));
Add("prepare-reschedule",Act("prepare-reschedule","Z9900010",1));
var trial=Add("reschedule",Act("reschedule","Z9900010",1));p=Adopt(trial);context.DemoPlan=p;
Check(p.NativeResultId==trial.GetProperty("calculationId").GetString(),"采用确切已展示试排结果");
Check(p.OrderIds[0]=="Z9900010","输入顺序按确认调整");
var adopted=Add("overview-after",NativeDemoWorkspace.Query(p,"overview"));
Check(adopted.GetProperty("calculationId").GetString()==p.NativeResultId,"计划查询指向已采用结果");
var risk=Add("risk-after",new ApsAnalysisTools(context).Execute("risk"));
var expectedRisk=NativeDemoWorkspace.Orders(NativeDemoGateway.Read(p.NativeResultId)).Count(o=>o.Risk);
Check(risk.GetProperty("orders").GetArrayLength()==expectedRisk,"风险由当前完成日期和交期比较得出");
Add("order-after",new ApsAnalysisTools(context).Execute("order","Z9900010"));
var material=Add("material-after",new BusinessMaterialReadiness(context).Execute("order","Z9900006"));
Check(material.GetProperty("mrpResultId").GetString()==p.NativeMrpResultId,"重排后仍能联查已保存独立MRP");
var tasks=Add("dispatch",new BusinessOrderDispatch(context).Execute(orderId:"Z9900026"));
Check(tasks.GetProperty("tasks").GetArrayLength()>0,"派工使用已有来源关联任务");
Add("prepare-release",Act("prepare-release"));
var release=Add("release",Act("release"));
Check(release.GetProperty("receipt").GetProperty("sourceResultId").GetString()==p.NativeResultId,"下发承接已展示计划");
p=Adopt(release);context.DemoPlan=p;
Check(p.ReleaseStatus=="released","确认下发后才更新下发状态");
var afterTasks=Add("dispatch-after",new BusinessOrderDispatch(context).Execute(orderId:"Z9900026"));
Check(afterTasks.GetProperty("tasks").GetArrayLength()==tasks.GetProperty("tasks").GetArrayLength(),"下发未虚构固定数量的新任务");
Check(NativeDemoWorkspace.Valid(p),"下发结果继续满足当前数据集一致性");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output,JsonSerializer.Serialize(new{status="PASS",modelInvoked=false,paidModelCalls=0,initialRiskCount=initialRisk.GetProperty("orders").GetArrayLength(),adoptedRiskCount=expectedRisk,checks,records,plan=p},new JsonSerializerOptions(NativeDemoGateway.Json){WriteIndented=true}));
Console.WriteLine($"PASS {checks.Count} checks; initial risk {initialRisk.GetProperty("orders").GetArrayLength()}, adopted risk {expectedRisk}; paid model calls 0");
