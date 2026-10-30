using WePilot.Agent.Agents;
using WePilot.Agent.Tools;
using Xunit;

namespace WePilot.Tests;

public class ReplyContractTests
{
    static DemoPlanState Plan()=>new(){SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen"};

    [Theory]
    [InlineData("可以直接投产，无需采购。")]
    [InlineData("不用补货，直接生产即可。")]
    [InlineData("已经买齐，可以生产。")]
    [InlineData("不需要补货，可以直接加工。")]
    public void BusinessAssertionsOutsideTheNavigationContractUseStructuredMaterialFacts(string draft)
    {
        var result=new BusinessMaterialReadiness(new(){DemoPlan=Plan()}).Execute("order","Z9900006");
        Assert.True(result.Success);
        var reply=NativeReplyGuard.Present(draft,result.UiPayload,"核对订单的物料");
        Assert.NotEqual(draft,reply);Assert.Contains("抵扣库存",reply);
    }

    [Theory]
    [InlineData("已送到车间执行，可以开始生产。")]
    [InlineData("交给车间了，不用再确认。")]
    [InlineData("车间已经收到，可以直接生产。")]
    [InlineData("已发给车间，直接加工即可。")]
    public void APreparationCannotClaimExecutionUsingAlternativeWords(string draft)
    {
        var result=new InteractiveDemoTools(new()).Execute("prepare-release",quantity:0,scenario:"frozen",plan:Plan(),version:NativeDemoWorkspace.Version);
        Assert.True(result.Success);
        var reply=NativeReplyGuard.Present(draft,result.UiPayload,"准备下发当前计划，先让我确认");
        Assert.NotEqual(draft,reply);Assert.Contains("确认",reply);
    }

    [Theory]
    [InlineData("请查看明细。")]
    [InlineData("可以展开查看详情。")]
    public void NavigationWithoutAdditionalBusinessAssertionsIsRetained(string draft)
    {
        var result=new ApsAnalysisTools(new(){DemoPlan=Plan()}).Execute("order","Z9900010");
        Assert.Equal(draft,NativeReplyGuard.Present(draft,result.UiPayload,"查看订单排程"));
    }
}
