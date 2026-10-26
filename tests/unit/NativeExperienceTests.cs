using System.Text.Json;
using WePilot.Agent.Agents;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;
public class NativeExperienceTests
{
    static DemoPlanState Plan()=>new(){SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen"};
    static JsonElement Spec(ToolResult result)=>JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec,new JsonSerializerOptions(JsonSerializerDefaults.Web));
    [Fact] public void PublicOverviewNeverContainsInternalInstructionsOrTraceIds()
    {
        var result=new ApsAnalysisTools(new(){DemoPlan=Plan()}).Execute("overview");Assert.True(result.Success);
        var spec=Spec(result);var text=spec.GetProperty("headline").GetString()!;
        var id=spec.GetProperty("calculationId").GetString()!;
        Assert.DoesNotContain("只使用",text);Assert.DoesNotContain(id,text);
        Assert.DoesNotContain("计算编号",text);Assert.DoesNotContain("GUID",text);
        Assert.Equal(2,spec.GetProperty("presentationVersion").GetInt32());
        Assert.DoesNotContain(id,result.ToModelJson());
    }
    [Theory]
    [InlineData("prepare-release","计划下发确认")]
    [InlineData("prepare-mrp","物料需求计算确认")]
    [InlineData("prepare-reschedule","订单顺序调整确认")]
    public void PreparationTitlesDescribeAnUnexecutedAction(string action,string title)
    {
        var result=new InteractiveDemoTools(new()).Execute(action,"Z9900010",position:1,quantity:0,scenario:"frozen",plan:Plan(),version:NativeDemoWorkspace.Version);
        Assert.True(result.Success);Assert.Equal(title,result.UiPayload.Single().Title);Assert.True(Spec(result).GetProperty("prepare").GetBoolean());
    }
    [Fact] public void MaterialFactsKeepRealArithmeticAndStateTheAvailableDisplay()
    {
        var result=new BusinessMaterialReadiness(new(){DemoPlan=Plan()}).Execute("order","Z9900006");Assert.True(result.Success);
        Assert.Contains("6020.7",result.Summary);Assert.Contains("3520.7",result.Summary);Assert.Contains("500",result.Summary);Assert.Contains("2000",result.Summary);
        var spec=Spec(result);Assert.DoesNotContain(spec.GetProperty("mrpResultId").GetString()!,result.ToModelJson());
        Assert.True(spec.TryGetProperty("capabilities",out var c));
        Assert.False(c.GetProperty("lineLoad").GetBoolean());Assert.False(c.GetProperty("dateViews").GetBoolean());
    }
    [Fact] public void NativePromptHasOneCurrentBusinessContract()
    {
        var field=typeof(AgentPrompts).GetField("NativeSystemPrompt");Assert.NotNull(field);var prompt=field!.GetRawConstantValue()!.ToString()!;
        Assert.DoesNotContain("13个",prompt);Assert.DoesNotContain("8张",prompt);
        Assert.Contains("先回答",prompt);Assert.Contains("不要",prompt);Assert.Contains("确认",prompt);
    }
    [Fact] public void TaskRowsCarryResolvedBusinessOrderNumbersForFiltering()
    {
        var result=new BusinessOrderDispatch(new(){DemoPlan=Plan()}).Execute(orderId:"Z9900026");
        Assert.True(result.Success);var tasks=Spec(result).GetProperty("tasks").EnumerateArray().ToArray();Assert.Equal(6,tasks.Length);
        Assert.All(tasks,t=>Assert.Contains("Z9900026",t.GetProperty("orderIds").EnumerateArray().Select(x=>x.GetString())));
    }
    [Theory]
    [InlineData("已把Z9900010调到第1位，试排预览已生成。")]
    [InlineData("将按冻结输入执行本地MRP。")]
    [InlineData("试排完成，可以采用方案。")]
    [InlineData("下发完成，计划提交成功。")]
    public void APreparationReplyCannotClaimExecutionOrLeakImplementation(string draft)
    {
        var result=new InteractiveDemoTools(new()).Execute("prepare-reschedule","Z9900010",position:1,quantity:0,scenario:"frozen",plan:Plan(),version:NativeDemoWorkspace.Version);
        var reply=NativeReplyGuard.Present(draft,result.UiPayload,"把Z9900010调整到第1位");
        Assert.DoesNotContain("预览已生成",reply);Assert.DoesNotContain("已把",reply);Assert.DoesNotContain("冻结",reply);Assert.Contains("确认",reply);
    }
    [Fact] public void AValidHumanReplyIsKeptAndUnrequestedCapabilityClaimsAreReplaced()
    {
        var result=new ApsAnalysisTools(new(){DemoPlan=Plan()}).Execute("order","Z9900010",source:"demo");
        const string draft="Z9900010的工序安排已查到，可以展开查看。";
        Assert.Equal(draft,NativeReplyGuard.Present(draft,result.UiPayload,"Z9900010怎么安排"));
        var corrected=NativeReplyGuard.Present("负载率和年月日视图已经展示。",result.UiPayload,"Z9900010怎么安排");
        Assert.DoesNotContain("负载率",corrected);Assert.Contains("Z9900010",corrected);
    }
    [Fact] public void ReleaseReplyCannotTurnPlanReleaseIntoFinishedProduction()
    {
        var blocks=new[]{new UiBlock{Spec=new{native=true,view="release",orders=Plan().OrderIds.Select(id=>new{orderId=id}).ToArray()}}};
        var reply=NativeReplyGuard.Present("已下发，12张订单全部完成。",blocks,"下发当前计划");
        Assert.DoesNotContain("全部完成",reply);Assert.Contains("计划已下发",reply);
    }
    [Fact] public void UnsavedMrpCannotClaimSavedSuccess()
    {
        var blocks=new[]{new UiBlock{Spec=new{native=true,view="mrp",mrpRows=Array.Empty<object>(),orders=Array.Empty<object>()}}};
        Assert.DoesNotContain("保存成功",NativeReplyGuard.Present("核算完成，结果保存成功。",blocks,"计算物料需求"));
    }
}
