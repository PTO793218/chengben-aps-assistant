using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using WePilot.Agent.Agents;
using Xunit;
namespace WePilot.Tests;
public class UrgentInsertTests
{
    static DemoPlanState Plan()=>new(){SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen"};
    static JsonElement Spec(ToolResult r)=>JsonSerializer.SerializeToElement(r.UiPayload.Single().Spec,NativeDemoGateway.Json);
    [Fact] public void InsertPreparesAnUnplannedOrderWithoutChangingCurrentPlan()
    {
        var p=Plan();var r=new InteractiveDemoTools(new()).Execute("prepare-insert","Z99100014",quantity:0,scenario:"frozen",plan:p,version:p.DemandVersion);
        Assert.True(r.Success);var s=Spec(r);Assert.Equal("insert",s.GetProperty("view").GetString());
        Assert.Equal(12,p.OrderIds.Length);Assert.Single(s.GetProperty("availableOrders").EnumerateArray());
        Assert.DoesNotContain("Z99100014",p.OrderIds);Assert.Contains("13",r.Summary);
    }
    [Fact] public void InsertRejectsExistingAndUnknownOrders()
    {
        foreach(var id in new[]{"Z9900010","Z99999999"})
            Assert.False(new InteractiveDemoTools(new()).Execute("prepare-insert",id,quantity:0,scenario:"frozen",plan:Plan(),version:NativeDemoWorkspace.Version).Success);
    }
    [Fact] public void ModelHasDistinctPrepareAndConfirmedInsertionFunctions()
    {
        Assert.NotNull(typeof(NativeDemoModelTools).GetMethod("PrepareInsertDemo"));
        Assert.NotNull(typeof(NativeDemoModelTools).GetMethod("SimulateInsertDemo"));
    }
    [Theory]
    [InlineData(true,"Z99100014已插入当前计划，订单现在是13张。")]
    [InlineData(false,"新订单已加入当前计划，现在生效了。")]
    [InlineData(false,"已将新订单Z99100014插入当前计划，共13张订单。")]
    [InlineData(false,"已经把Z99100014加入当前计划。")]
    public void InsertCannotClaimPlanChangedBeforeAdoption(bool prepare,string draft)
    {
        var blocks=new[]{new UiBlock{Spec=new{native=true,view="insert",prepare,orderId="Z99100014",position=1,orders=new[]{new{orderId="Z99100014"}},allOrders=Plan().OrderIds.Select(orderId=>new{orderId}).ToArray(),beforeOrderCount=12,afterOrderCount=13,changes=Array.Empty<object>()}}};
        var shown=NativeReplyGuard.Present(draft,blocks,"把新来的急单插入当前计划，先看影响");
        Assert.NotEqual(draft,shown);Assert.Contains("确认",shown);Assert.DoesNotContain("已插入当前计划",shown);Assert.DoesNotContain("已加入当前计划",shown);
    }
}
