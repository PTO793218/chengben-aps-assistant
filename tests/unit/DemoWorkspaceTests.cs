using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;
namespace WePilot.Tests;
public sealed class DemoWorkspaceTests
{
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static JsonElement Spec(ToolResult r){Assert.True(r.Success,r.Summary);return JsonSerializer.SerializeToElement(r.UiPayload.Single().Spec,Json);}
 [Fact] public void EveryQueryUsesSameCalculationBeforeAndAfterChanges()
 {
  foreach(var p in new[]{new DemoPlanState(),new DemoPlanState{SupplyScenario="supply",Revision=2,OrderIds=CoupledDemoEngine.LockedIds.Concat(new[]{"Z9900003","Z9900005","Z9900002","Z9900013"}).ToArray(),DemandQuantities=new(){{"Z9900003",8000}}}})
  {
   var c=new ToolExecutionContext{DemoPlan=p};var expected=CoupledDemoEngine.Run(p);
   foreach(var r in new[]{new ApsAnalysisTools(c).Execute("overview"),new ApsAnalysisTools(c).Execute("risk"),new ApsAnalysisTools(c).Execute("order","Z9900003",source:"demo"),new BusinessMaterialReadiness(c).Execute("order","Z9900003"),new BusinessOrderDispatch(c).Execute(orderId:"Z9900003")})
   {var s=Spec(r);Assert.Equal(expected.CalculationId,s.GetProperty("calculationId").GetString());Assert.Equal(expected.Orders.Single(o=>o.OrderId=="Z9900003").PlanEnd,s.GetProperty("workspace").GetProperty("orders").EnumerateArray().First(o=>o.GetProperty("orderId").GetString()=="Z9900003").GetProperty("planEnd").GetString());}
   Assert.False(new ApsAnalysisTools(c).Execute("order","Z9900003",version:"unknown").Success);
   Assert.Contains("Z9900001",new ApsAnalysisTools(c).Execute("order","Z9900001",source:"demo").Summary);
  }
 }
 [Fact] public void ReleasedTasksComeFromExactPlanRecordsAndDoNotClaimStarted()
 {
  var p=new DemoPlanState{SupplyScenario="supply",OrderIds=CoupledDemoEngine.LockedIds.Concat(new[]{"Z9900003","Z9900005","Z9900002","Z9900013"}).ToArray()};
  var release=Spec(new InteractiveDemoTools(new()).Execute("release",plan:p));
  var adopted=JsonSerializer.Deserialize<DemoPlanState>(release.GetProperty("proposedPlan").GetRawText(),Json)!;
  Assert.Equal("released",adopted.ReleaseStatus);var run=CoupledDemoEngine.Run(adopted);
  var spec=Spec(new BusinessOrderDispatch(new(){DemoPlan=adopted}).Execute());
  Assert.Equal(36,spec.GetProperty("taskRows").GetArrayLength());
  foreach(var t in spec.GetProperty("taskRows").EnumerateArray())
  {
   Assert.Equal(t.GetProperty("locked").GetBoolean()?CoupledDemoEngine.LockedCalculationId:run.CalculationId,t.GetProperty("sourceCalculationId").GetString());Assert.Equal(0,t.GetProperty("deliveryQuantity").GetInt32());
   Assert.Equal(t.GetProperty("locked").GetBoolean()?"已开工锁定作业（模拟）":"待执行（模拟）",t.GetProperty("taskStatus").GetString());
   Assert.Equal(t.GetProperty("locked").GetBoolean()?"run":"created",t.GetProperty("statusKey").GetString());
   var sourceIds=t.GetProperty("sourcePlanRecordIds").EnumerateArray().Select(i=>i.GetString()).ToArray();
   Assert.NotEmpty(sourceIds);Assert.All(sourceIds,id=>Assert.Contains(run.Tasks,x=>x.PlanRecordId==id));
   Assert.Equal(t.GetProperty("taskQuantity").GetInt32(),run.Tasks.Where(x=>sourceIds.Contains(x.PlanRecordId)).Sum(x=>x.Quantity));
  }
  foreach(var action in new[]{"release","reschedule","mrp"})Assert.False(new InteractiveDemoTools(new()).Execute(action,"Z9900003",plan:adopted).Success);
 }
 [Theory][InlineData("SIM-L1")][InlineData("SIM-L2")][InlineData("SIM-L3")]
 public void EveryLineFollowsCurrentTasksWithoutDuplicatingOrders(string line)
 {
  var p=new DemoPlanState{SupplyScenario="supply"};p.ReleaseStatus="released";p.ReleasedCalculationId=CoupledDemoEngine.CalculationId(p);
  var spec=Spec(new BusinessOrderDispatch(new(){DemoPlan=p}).Execute(line,"Z9900003"));
  Assert.Single(spec.GetProperty("orderRows").EnumerateArray());Assert.Single(spec.GetProperty("taskRows").EnumerateArray());
 }
 [Fact] public void CorruptedOrLegacyStateNeverFallsBackToHistoricalResults()
 {
  foreach(var p in new[]{JsonSerializer.Deserialize<DemoPlanState>("{\"mrpInputs\":[null]}",Json)!,new DemoPlanState{SchemaVersion=1},new DemoPlanState{DemandQuantities=null!}})
  {
   Assert.False(InteractiveDemoTools.ValidPlan(p));var c=new ToolExecutionContext{DemoPlan=p};
   Assert.False(new BusinessMaterialReadiness(c).Execute("order","Z9900003").Success);Assert.Empty(c.UiPayload);
  }
 }
 [Fact] public void RealQueriesNeverUseCurrentSimulatedWorkspace()
 {
  var c=new ToolExecutionContext{DemoPlan=new(),RealSourceRequested=true};
  Assert.False(new ApsAnalysisTools(c).Execute("order","Z9900003",source:"demo").Success);
  Assert.False(new BusinessMaterialReadiness(c).Execute("order","Z9900003").Success);
  Assert.False(new BusinessOrderDispatch(c).Execute(orderId:"Z9900003").Success);Assert.Empty(c.UiPayload);
 }
}
