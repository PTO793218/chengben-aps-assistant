using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;
public class NativeDemoTests
{
    static DemoPlanState Plan()=>new(){SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=new[]{"Z9900021","Z9900006","Z9900012","Z9900009","Z9900025","Z9900010","Z9900026","Z9900024","Z9900019","Z9900020","Z9900022","Z9900023"},SupplyScenario="frozen"};
    static JsonElement Spec(ToolResult result)=>JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    [Fact] public void CurrentNativeWorkspaceRecognizesTwelveOrdersAndActualInProgressTag()
    {
        var p=Plan(); Assert.True(InteractiveDemoTools.ValidPlan(p));
        var r=new ApsAnalysisTools(new(){DemoPlan=p}).Execute("overview");Assert.True(r.Success);
        var s=Spec(r);Assert.True(s.GetProperty("native").GetBoolean());
        Assert.Equal(12,s.GetProperty("orders").GetArrayLength());
        Assert.Single(s.GetProperty("orders").EnumerateArray().Where(o=>o.GetProperty("taskTag").GetString()=="T"));
        Assert.DoesNotContain("8张",r.Summary);
    }
    [Fact] public void MrpConfirmationHasNoSupplyPickerOrDemandOverride()
    {
        var c=new ToolExecutionContext{DemoPlan=Plan()};var plugin=new InteractiveDemoModelTools(c);
        Assert.False(ToolResult.FromModelJson(plugin.PrepareMrpDemo("Z9900010",inventory:10000)).Success);
        Assert.False(ToolResult.FromModelJson(plugin.PrepareMrpDemo("Z9900010",quantity:1000)).Success);
        var r=ToolResult.FromModelJson(plugin.PrepareMrpDemo("Z9900010"));Assert.True(r.Success);
        var s=Spec(new InteractiveDemoTools(new()).Execute("prepare-mrp","Z9900010",quantity:0,scenario:"frozen",plan:Plan(),version:NativeDemoWorkspace.Version));
        Assert.True(s.GetProperty("native").GetBoolean());Assert.False(s.TryGetProperty("scenarios",out _));
    }
    [Fact] public void SavedMrpQueryHasIndependentSourceAndNeverRecalculatesSchedule()
    {
        var p=Plan();var r=new BusinessMaterialReadiness(new(){DemoPlan=p}).Execute("order","Z9900010");Assert.True(r.Success);
        var s=Spec(r);Assert.NotEmpty(s.GetProperty("mrpRows").EnumerateArray());
        Assert.Equal("261016902",s.GetProperty("sourceVersion").GetString());
        Assert.Equal("独立MRP",s.GetProperty("mrpSource").GetString());
        Assert.Equal("unreleased",p.ReleaseStatus);Assert.Equal(0,p.Revision);
        Assert.DoesNotContain("锁定库存",r.Summary);
    }
    [Fact] public void InputTamperingAndUnknownResultsFailClosed()
    {
        var p=Plan();p.DemandQuantities["Z9900010"]=1000;Assert.False(InteractiveDemoTools.ValidPlan(p));
        p=Plan();p.SupplyScenario="supply";Assert.False(InteractiveDemoTools.ValidPlan(p));
        p=Plan();p.ReleaseStatus="released";Assert.False(InteractiveDemoTools.ValidPlan(p));
    }
    [Fact] public void NativeModelSchemaCannotInventSupplyOrDemandArguments()
    {
        var type=typeof(InteractiveDemoTools).Assembly.GetType("WePilot.Agent.Tools.NativeDemoModelTools");Assert.NotNull(type);
        var method=type!.GetMethod("PrepareMrpDemo");Assert.NotNull(method);
        Assert.Equal(new[]{"orderId"},method!.GetParameters().Select(p=>p.Name).ToArray());
    }
    [Fact] public void UnrecoverableUnitCaptionUsesOriginalUnitCode()
    {
        var r=new BusinessMaterialReadiness(new(){DemoPlan=Plan()}).Execute("order","Z9900010");var s=Spec(r);
        Assert.All(s.GetProperty("mrpRows").EnumerateArray(),row=>Assert.DoesNotContain("\uFFFD",row.GetProperty("unit").GetString()));
    }
    [Fact] public void MaterialFilterLimitsModelFactsAndBothUiSources()
    {
        var r=new BusinessMaterialReadiness(new(){DemoPlan=Plan()}).Execute("material",materialQuery:"C.02.02.0031");Assert.True(r.Success);
        var s=Spec(r);Assert.Single(s.GetProperty("orders").EnumerateArray());
        Assert.Contains("Z9900006",r.Summary);Assert.DoesNotContain("C.02.02.0010",r.Summary);
        Assert.All(s.GetProperty("mrpRows").EnumerateArray(),x=>Assert.Equal("C.02.02.0031",x.GetProperty("material").GetString()));
        Assert.All(s.GetProperty("materialRows").EnumerateArray(),x=>Assert.Equal("C.02.02.0031",x.GetProperty("material_no").GetString()));
        Assert.False(new BusinessMaterialReadiness(new(){DemoPlan=Plan()}).Execute("material",materialQuery:"不存在的材料XYZ").Success);
    }
    [Fact] public void FutureStartWithoutEndReturnsAnEmptyValidWindow()
    {
        var r=new ApsAnalysisTools(new(){DemoPlan=Plan()}).Execute("overview",startDate:"2026-11-01");Assert.True(r.Success);
        Assert.Empty(Spec(r).GetProperty("chart").GetProperty("items").EnumerateArray());
    }
    [Fact] public void MrpSourceIsExplicitlyIndependentAndUnnumbered()
    {
        var r=new BusinessMaterialReadiness(new(){DemoPlan=Plan()}).Execute("order","Z9900006");var s=Spec(r);
        Assert.True(s.TryGetProperty("mrpSourceLabel",out var label));Assert.Contains("未编号",label.GetString());
        Assert.Equal("",s.GetProperty("mrpSourceVersion").GetString());
    }
    [Fact] public void RescheduleKeepsWholeBatchScopeAndTargetIdentitySeparate()
    {
        var r=new InteractiveDemoTools(new()).Execute("prepare-reschedule","Z9900010",position:1,quantity:0,scenario:"frozen",plan:Plan(),version:NativeDemoWorkspace.Version);
        Assert.True(r.Success);var s=Spec(r);
        Assert.Equal("Z9900010",s.GetProperty("orderId").GetString());
        Assert.Equal(12,s.GetProperty("orders").GetArrayLength());
        Assert.Equal(12,s.GetProperty("workspace").GetProperty("orders").GetArrayLength());
        Assert.Equal(12,s.GetProperty("chart").GetProperty("rows").EnumerateArray().Select(x=>x.GetProperty("orderId").GetString()).Distinct().Count());
    }
}
