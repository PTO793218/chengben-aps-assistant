using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;
namespace WePilot.Tests;
public sealed class CoupledPlannerTests
{
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static JsonElement Spec(ToolResult r){Assert.True(r.Success,r.Summary);return JsonSerializer.SerializeToElement(r.UiPayload.Single().Spec,Json);}
 [Fact] public void EntireProductionPlanUsesTwelveUnfinishedOrdersAndFourMaterialBlockedOrders()
 {
  var p=new DemoPlanState();var run=CoupledDemoEngine.Run(p);
  Assert.Equal(12,run.Orders.Length);Assert.Equal(8,run.Orders.Count(o=>o.IsLocked));Assert.Equal(4,run.Orders.Count(o=>!o.IsLocked));
  Assert.DoesNotContain(run.Orders,o=>o.OrderId=="Z9900001");Assert.Contains(run.Orders,o=>o.OrderId=="Z9900002");Assert.Contains(run.Orders,o=>o.OrderId=="Z9900006");
  Assert.Equal(4020.39m,run.Simulation!.ReservedStock);Assert.Equal(53049.61m,run.Simulation.GrossNeed);Assert.Equal(43049.61m,run.Simulation.PurchaseNeed);
  var ctx=new ToolExecutionContext{DemoPlan=p};var all=Spec(new ApsAnalysisTools(ctx).Execute("overview"));Assert.Equal(4,all.GetProperty("simulation").GetProperty("unscheduled").GetInt32());
  Assert.False(new ApsAnalysisTools(ctx).Execute("overview",scheduleScope:"incomplete").Success);
  var material=Spec(new BusinessMaterialReadiness(ctx).Execute("order","Z9900003"));Assert.Equal(run.CalculationId,material.GetProperty("calculationId").GetString());Assert.Equal(6886.13m,material.GetProperty("workspace").GetProperty("orders")[0].GetProperty("material").GetProperty("purchaseNeed").GetDecimal());
 }
 [Fact] public void PriorityReallocatesSharedSupplyAndRespectsExistingCapacity()
 {
  var p=new DemoPlanState{SupplyScenario="supply"};var result=Spec(new InteractiveDemoTools(new()).Execute("reschedule","Z9900003",position:1,plan:p));
  Assert.Equal(12,result.GetProperty("simulation").GetProperty("fullyScheduled").GetInt32());
  var target=result.GetProperty("after").GetProperty("orders").EnumerateArray().Single(o=>o.GetProperty("orderId").GetString()=="Z9900003");
  Assert.Equal("2026-10-06",target.GetProperty("planEnd").GetString());Assert.Equal(5979.61m,target.GetProperty("material").GetProperty("stockUsed").GetDecimal());Assert.Equal("2026-10-04",target.GetProperty("material").GetProperty("readyDate").GetString());
  Assert.Equal("2026-10-05",target.GetProperty("planStart").GetString());
  foreach(var o in result.GetProperty("after").GetProperty("orders").EnumerateArray())Assert.True(string.CompareOrdinal(o.GetProperty("planStart").GetString(),o.GetProperty("material").GetProperty("readyDate").GetString())>=0);
 }
 [Fact] public void ExistingLockedWorkAndMaterialAreIdenticalAfterSupplyAndOrderChanges()
 {
  var p=new DemoPlanState{SupplyScenario="supply"};var before=CoupledDemoEngine.Run(p);
  var candidate=Spec(new InteractiveDemoTools(new()).Execute("reschedule","Z9900003",plan:p));var after=JsonSerializer.Deserialize<DemoPlanState>(candidate.GetProperty("proposedPlan").GetRawText(),Json)!;
  after.SupplyScenario="late";var run=CoupledDemoEngine.Run(after);
  foreach(var o in before.Orders.Where(o=>o.IsLocked)){Assert.Equal(JsonSerializer.Serialize(o,Json),JsonSerializer.Serialize(run.Orders.Single(x=>x.OrderId==o.OrderId),Json));}
  Assert.Equal(JsonSerializer.Serialize(before.Tasks.Where(t=>CoupledDemoEngine.LockedRemaining(t.OrderId)>0),Json),JsonSerializer.Serialize(run.Tasks.Where(t=>CoupledDemoEngine.LockedRemaining(t.OrderId)>0),Json));
  var none=CoupledDemoEngine.Run(new(){SupplyScenario="none"});Assert.Equal(8,none.Simulation!.FullyScheduled);Assert.Equal(4020.39m,none.Simulation.StockUsed);
 }
 [Fact] public void LockedOrdersCannotMoveChangeQuantityOrBeDropped()
 {
  var engine=new InteractiveDemoTools(new());Assert.False(engine.Execute("reschedule","Z9900006").Success);
  Assert.False(engine.Execute("mrp","Z9900006",quantity:419,scenario:"supply").Success);
  Assert.True(engine.Execute("mrp","Z9900006",quantity:420,scenario:"supply").Success);
  Assert.False(InteractiveDemoTools.ValidPlan(new(){OrderIds=InteractiveDemoTools.InitialIds.Where(id=>id!="Z9900002").ToArray()}));
  Assert.False(engine.Execute("release").Success);
 }
 [Fact] public void MaterialReadyQueryUsesCalculatedReasonsAfterIncompleteSceneRemoval()
 {
  var p=new DemoPlanState{SupplyScenario="supply"};
  Assert.False(new ApsAnalysisTools(new(){DemoPlan=p}).Execute("overview",scheduleScope:"incomplete").Success);
  var m=Spec(new BusinessMaterialReadiness(new(){DemoPlan=p}).Execute());
  Assert.Equal(12,m.GetProperty("model").GetProperty("summary").GetProperty("readyOrders").GetInt32());
  Assert.DoesNotContain("未重新计算",m.GetProperty("headline").GetString());
 }
 [Theory][InlineData("在途为60000")][InlineData("在途数量60000")][InlineData("在途：60000")]
 public void ParsedTransitIsNeverSilentlyIgnored(string input)
 {
  var s=Spec(new InteractiveDemoTools(new()).Route("MRP计算演示 Z9900003，库存为10000，"+input,null)!);
  Assert.Equal("supply",s.GetProperty("scenario").GetString());
  Assert.Equal(12500,s.GetProperty("quantity").GetInt32());
 }
 [Theory][InlineData("库存数量10000，无在途")][InlineData("库存为10000，在途数量60000")]
 public void SupplyNumbersDoNotOverwriteCurrentFinishedDemand(string message)
 {
  var p=new DemoPlanState{DemandQuantities=new(){{"Z9900003",8000}}};
  var s=Spec(new InteractiveDemoTools(new()).Route("MRP演示 Z9900003，"+message,p)!);
  Assert.Equal(8000,s.GetProperty("quantity").GetInt32());
 }
}
