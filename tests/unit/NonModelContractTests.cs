using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;

public class NonModelContractTests
{
    static DemoPlanState Plan()=>new(){SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen"};
    static JsonElement Spec(ToolResult result)=>JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec,NativeDemoGateway.Json);

    [Fact]
    public void DateFilteredOverviewSummaryUsesTheSameOrderScopeAsItsDetails()
    {
        var date=NativeDemoWorkspace.BusinessDate;
        var result=NativeDemoWorkspace.Query(Plan(),"overview",startDate:date,endDate:date);
        Assert.True(result.Success);
        var count=Spec(result).GetProperty("orders").GetArrayLength();
        Assert.InRange(count,1,NativeDemoWorkspace.Ids.Length-1);
        Assert.StartsWith($"当前范围{count}张订单中，",result.Summary);
        Assert.StartsWith($"当前范围{count}张订单中，",Spec(result).GetProperty("headline").GetString());
    }

    [Fact]
    public void EmptyDateFilteredOverviewDoesNotClaimTheWholeBatchHasNoPlan()
    {
        var result=NativeDemoWorkspace.Query(Plan(),"overview",startDate:"2027-02-01",endDate:"2027-02-01");
        Assert.True(result.Success);
        Assert.Empty(Spec(result).GetProperty("orders").EnumerateArray());
        Assert.StartsWith("当前范围0张订单中，",result.Summary);
    }

    [Fact]
    public void DispatchLineWhitespaceDoesNotHideExistingLinkedTasks()
    {
        var line=NativeDemoGateway.Baseline.GetProperty("tasks")[0].GetProperty("product_line_no").GetString()!;
        var plain=new BusinessOrderDispatch(new(){DemoPlan=Plan()}).Execute(line);
        var padded=new BusinessOrderDispatch(new(){DemoPlan=Plan()}).Execute(" "+line+" ");
        Assert.True(plain.Success);Assert.True(padded.Success);
        var plainTasks=Spec(plain).GetProperty("tasks");
        Assert.NotEmpty(plainTasks.EnumerateArray());
        Assert.Equal(plainTasks.GetRawText(),Spec(padded).GetProperty("tasks").GetRawText());
        Assert.Equal(plain.Summary,padded.Summary);
    }
}
