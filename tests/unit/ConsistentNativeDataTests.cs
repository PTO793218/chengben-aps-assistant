using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;
public class ConsistentNativeDataTests
{
    static DemoPlanState Plan()=>new(){SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen"};
    static JsonElement Spec(ToolResult r)=>JsonSerializer.SerializeToElement(r.UiPayload.Single().Spec,NativeDemoGateway.Json);
    [Fact] public void NewPlanMrpAndSourcesUseOneDatasetAndBusinessClock()
    {
        var config=NativeDemoGateway.Config;var baseline=NativeDemoGateway.Baseline;var mrp=NativeDemoGateway.MrpBaseline;
        Assert.Equal("2026-10-19",config.GetProperty("businessDate").GetString());
        foreach(var result in new[]{baseline,mrp})
        {
            Assert.Equal(config.GetProperty("fixtureId").GetString(),result.GetProperty("fixtureId").GetString());
            Assert.Equal(config.GetProperty("inputFingerprint").GetString(),result.GetProperty("inputFingerprint").GetString());
            Assert.StartsWith(config.GetProperty("businessDate").GetString()!,result.GetProperty("asOf").GetString());
        }
        Assert.Equal(config.GetProperty("demandVersion").GetString(),NativeDemoWorkspace.Version);
        var expected=config.GetProperty("orders").EnumerateArray().ToDictionary(x=>x.GetProperty("orderId").GetString()!,x=>x.GetProperty("dueDate").GetString());
        Assert.All(NativeDemoWorkspace.Orders(baseline),o=>Assert.Equal(expected[o.OrderId],o.DueDate));
        Assert.All(mrp.GetProperty("mrp").EnumerateArray(),r=>Assert.StartsWith(expected[r.GetProperty("sale_order_no").GetString()!]!,r.GetProperty("order_delivery_date").GetString()));
    }
    [Fact] public void RiskQueryIsDerivedFromCalculatedCompletionAndConfiguredDeadline()
    {
        var p=Plan();Assert.True(NativeDemoWorkspace.Valid(p));
        var actual=NativeDemoWorkspace.Orders(NativeDemoGateway.Baseline);
        Assert.All(actual,o=>Assert.Equal(string.CompareOrdinal(o.PlanEnd,o.DueDate)>0,o.Risk));
        var risk=Spec(new ApsAnalysisTools(new(){DemoPlan=p}).Execute("risk"));
        Assert.Equal(actual.Count(o=>o.Risk),risk.GetProperty("orders").GetArrayLength());
    }
    [Fact] public void HistoricalNativeVersionCannotBeUsedAsCurrentInputs()
    {
        var old=Plan();old.DemandVersion="SIM-261016-12A";
        Assert.False(NativeDemoWorkspace.Valid(old));
    }
}
