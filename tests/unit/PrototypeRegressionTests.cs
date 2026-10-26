using System.Text.Json;
using WePilot.Agent.Agents;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;
public class PrototypeRegressionTests
{
    static DemoPlanState Plan()=>new(){SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen"};

    [Theory]
    [InlineData("本批12张订单均有计划，0张存在交期风险，可以查看明细。")]
    [InlineData("这批订单没有交期风险。")]
    public void OverviewReplyUsesActualRiskCount(string draft)
    {
        var result=new ApsAnalysisTools(new(){DemoPlan=Plan()}).Execute("overview");
        Assert.True(result.Success);
        var spec=JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec,NativeDemoGateway.Json);
        var risks=spec.GetProperty("orders").EnumerateArray().Count(o=>o.GetProperty("risk").GetBoolean());
        var reply=NativeReplyGuard.Present(draft,result.UiPayload,"生产计划怎么样？");
        Assert.NotEqual(draft,reply);
        Assert.Contains($"{risks}张存在交期风险",reply);
    }

    [Fact]
    public void AnUnknownOrderImmediatelyBeforeChineseTextIsNotNavigationProse()
    {
        var result=new ApsAnalysisTools(new(){DemoPlan=Plan()}).Execute("order","Z9900006",source:"demo");
        var reply=NativeReplyGuard.Present("Z29999999的工序安排已查到，可以展开查看。",result.UiPayload,"查看Z9900006");
        Assert.DoesNotContain("Z29999999",reply);
        Assert.Contains("Z9900006",reply);
    }

    [Theory]
    [InlineData("订单Z9900006的物料库存充足，剩余需求为0公斤。")]
    [InlineData("订单Z9900006的物料已齐套，可以开工。")]
    [InlineData("订单Z9900006库存抵扣3520.7公斤，剩余需求500公斤。")]
    public void MaterialReplyCannotChangeQuantitiesOrInferReadiness(string draft)
    {
        var result=new BusinessMaterialReadiness(new(){DemoPlan=Plan()}).Execute("order","Z9900006");
        Assert.True(result.Success);
        var reply=NativeReplyGuard.Present(draft,result.UiPayload,"核对Z9900006的物料");
        Assert.NotEqual(draft,reply);
        Assert.Contains("抵扣库存500和在途2000后，还需3520.7公斤",reply);
        Assert.DoesNotContain("可以开工",reply);
    }

    [Theory]
    [InlineData("schedule")]
    [InlineData("material")]
    [InlineData("dispatch")]
    public void CurrentWorkspaceNeverFallsBackToHistoricalOrder(string view)
    {
        var context=new ToolExecutionContext{DemoPlan=Plan()};
        var result=view switch{
            "material"=>new BusinessMaterialReadiness(context).Execute("order","Z9900005"),
            "dispatch"=>new BusinessOrderDispatch(context).Execute(orderId:"Z9900005"),
            _=>new ApsAnalysisTools(context).Execute("order","Z9900005",source:"demo")};
        Assert.False(result.Success);
        Assert.Empty(result.UiPayload);
        Assert.Contains("未查到",result.Summary);
    }

    [Fact]
    public void ExplicitHistoricalViewStillCanReadHistoricalOrder()
    {
        var result=new ApsAnalysisTools(new()).Execute("order","Z9900005",source:"demo");
        Assert.True(result.Success);
        Assert.Equal("aps-analysis",result.UiPayload.Single().Type);
    }

    [Theory]
    [InlineData("prepare-mrp")]
    [InlineData("mrp")]
    public void MrpRejectsAnOrderNotYetAdoptedIntoCurrentPlan(string action)
    {
        var plan=Plan();
        var result=new InteractiveDemoTools(new()).Execute(action,"Z99100014",quantity:0,scenario:"frozen",plan:plan,version:plan.DemandVersion);
        Assert.False(result.Success);
        Assert.Empty(result.UiPayload);
        Assert.Contains("尚未加入当前计划",result.Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Z9900006")]
    public void MrpStillPreparesCurrentBatchAndItsExistingOrders(string id)
    {
        var plan=Plan();
        var result=new InteractiveDemoTools(new()).Execute("prepare-mrp",id,quantity:0,scenario:"frozen",plan:plan,version:plan.DemandVersion);
        Assert.True(result.Success);
    }
}
