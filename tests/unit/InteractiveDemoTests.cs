using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;
namespace WePilot.Tests;
public sealed class InteractiveDemoTests
{
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static JsonElement Spec(ToolResult r){Assert.True(r.Success,r.Summary);return JsonSerializer.SerializeToElement(r.UiPayload.Single().Spec,Json);}
 [Fact] public void WholeBatchPreservesCapacityDemandAndPrecedence()
 {
  var r=new InteractiveDemoTools(new()).Execute("reschedule","Z9900003",plan:new(){SupplyScenario="supply"});var s=Spec(r);
  foreach(var plan in new[]{s.GetProperty("before"),s.GetProperty("after")})
  {
   var tasks=plan.GetProperty("tasks").EnumerateArray().ToArray();
   Assert.All(tasks.GroupBy(t=>(t.GetProperty("stageIndex").GetInt32(),t.GetProperty("slot").GetInt32())),g=>Assert.Single(g));
   foreach(var order in plan.GetProperty("orders").EnumerateArray())
   {
    var stages=tasks.Where(t=>t.GetProperty("orderId").GetString()==order.GetProperty("orderId").GetString()).GroupBy(t=>t.GetProperty("stageIndex").GetInt32()).OrderBy(g=>g.Key).ToArray();
    Assert.Equal(3,stages.Length);Assert.All(stages,g=>Assert.Equal(order.GetProperty("quantity").GetInt32(),g.Sum(t=>t.GetProperty("quantity").GetInt32())));
    for(var i=1;i<3;i++)Assert.True(stages[i].Min(t=>t.GetProperty("slot").GetInt32())>stages[i-1].Max(t=>t.GetProperty("slot").GetInt32()));
    Assert.All(stages.SelectMany(g=>g),t=>Assert.InRange(t.GetProperty("quantity").GetInt32(),1,t.GetProperty("capacity").GetInt32()));
   }
  }
  Assert.DoesNotContain("uiPayload",r.ToModelJson());
 }
 [Theory][InlineData("none",49029.22)][InlineData("stock",43049.61)][InlineData("supply",0)][InlineData("late",0)]
 public void SharedAllocationConservesSupplyAndRepeatedRunsDoNotConsume(string scenario,decimal gap)
 {
  var p=new DemoPlanState{SupplyScenario=scenario};var run=CoupledDemoEngine.Run(p);
  Assert.Equal(gap,run.Simulation!.PurchaseNeed);Assert.Equal(run.Simulation.GrossNeed,run.Simulation.StockUsed+run.Simulation.TransitUsed+gap);
  Assert.True(run.Simulation.StockUsed<=run.Simulation.Stock);Assert.True(run.Simulation.TransitUsed<=run.Simulation.Transit);
  foreach(var lot in run.SupplyLots)Assert.True(run.Orders.Sum(o=>o.Material!.Allocations.Where(a=>a.SupplyOrderNo==lot.Id).Sum(a=>a.Quantity))<=lot.Quantity);
  Assert.Equal(JsonSerializer.Serialize(run,Json),JsonSerializer.Serialize(CoupledDemoEngine.Run(p),Json));
 }
 [Fact] public void LateSupplyAndChangedDemandAffectOtherOrdersAndRisk()
 {
  var normal=CoupledDemoEngine.Run(new(){SupplyScenario="supply"});var late=CoupledDemoEngine.Run(new(){SupplyScenario="late"});
  Assert.True(string.CompareOrdinal(late.Orders.Single(o=>o.OrderId=="Z9900013").PlanEnd,normal.Orders.Single(o=>o.OrderId=="Z9900013").PlanEnd)>0);
  var changed=CoupledDemoEngine.Run(new(){DemandQuantities=new(){{"Z9900005",1000}}});
  Assert.Equal(550.89m,changed.Orders.Single(o=>o.OrderId=="Z9900005").Material!.GrossNeed);Assert.True(changed.Orders.Single(o=>o.OrderId=="Z9900002").Material!.StockUsed>CoupledDemoEngine.Run(new()).Orders.Single(o=>o.OrderId=="Z9900002").Material!.StockUsed);
  Assert.Equal(1000,changed.Tasks.Where(t=>t.OrderId=="Z9900005"&&t.StageIndex==0).Sum(t=>t.Quantity));
  var huge=CoupledDemoEngine.Run(new(){SupplyScenario="supply",DemandQuantities=new(){{"Z9900013",100000}}});Assert.True(huge.Orders.Single(o=>o.OrderId=="Z9900013").Risk);
 }
 [Fact] public void InvalidInputsNeverFallback()
 {
  var t=new InteractiveDemoTools(new());Assert.False(t.Execute("reschedule","Z9900003",version:"SIM-RELEASED").Success);Assert.False(t.Execute("reschedule","Z9900003",version:"unknown").Success);
  Assert.False(t.Execute("mrp","Z9999999").Success);Assert.False(t.Execute("mrp","Z9900003",quantity:0).Success);Assert.False(t.Execute("mrp","Z9900003",scenario:"unknown").Success);
  Assert.False(t.Execute("reschedule","Z9900003",plan:new(){OrderIds=new[]{"Z9900003"}}).Success);
 }
 [Theory][InlineData("真实订单 Z9900003 重排",true)][InlineData("演示订单 Z9900003 插队",true)][InlineData("MRP 计算演示",true)][InlineData("真实 MRP 计算",true)][InlineData("查询演示订单 Z9900006 的派工",false)][InlineData("查看已保存MRP结果",false)][InlineData("查询当前MRP需求",false)]
 public void GuardRoutesOnlyOperations(string m,bool handled){var r=new InteractiveDemoTools(new()).Route(m,null);Assert.Equal(handled,r!=null);if(m.Contains("真实")){Assert.False(r!.Success);Assert.Empty(r.UiPayload);}}
 [Theory][InlineData("MRP 演示 Z9900013 需求数量12500",12500,"stock")][InlineData("MRP 演示 Z9900013",50000,"stock")][InlineData("MRP 演示 Z9900003，无库存无在途",12500,"none")][InlineData("MRP 演示 Z9900003，有库存10000 kg，无在途",12500,"stock")][InlineData("MRP 演示 Z9900003，有库存10000 kg，有在途60000 kg",12500,"supply")]
 public void NaturalInputsPreserved(string m,int q,string scenario){var s=Spec(new InteractiveDemoTools(new()).Route(m,null)!);Assert.Equal(q,s.GetProperty("quantity").GetInt32());Assert.Equal(scenario,s.GetProperty("scenario").GetString());}
 [Theory][InlineData("MRP 演示 Z9900003，有库存500，无在途")][InlineData("MRP 演示 Z9900003，有库存1000.5，无在途")][InlineData("MRP 演示 Z9900003，有库存-1000，无在途")]
 public void UnsupportedQuantitiesNeverSubstitute(string m)=>Assert.False(new InteractiveDemoTools(new()).Route(m,null)!.Success);
}
