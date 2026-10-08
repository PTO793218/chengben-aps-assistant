using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;
namespace WePilot.Tests;
public sealed class ProductionToolTests
{
    [Fact]
    public void SnapshotSummaryAndUiAreSeparated()
    {
        var context = new ToolExecutionContext(); var result = new ProductionTools(context).Execute();
        Assert.True(result.Success); Assert.Single(result.UiPayload); Assert.Equal("production-plan", result.UiPayload[0].Type);
        using var json = JsonDocument.Parse(result.ToModelJson());
        var facts = json.RootElement.GetProperty("agentContext");
        Assert.Equal(12, facts.GetProperty("orderCount").GetInt32());
        Assert.Equal(8, facts.GetProperty("fullyScheduled").GetInt32());
        Assert.Equal(10, facts.GetProperty("pendingCount").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("uiPayload", out _)); Assert.Single(context.ToolCalls);
        Assert.False(facts.GetProperty("detailedTasksIncluded").GetBoolean());
        Assert.True(facts.GetProperty("scheduledTaskCount").GetInt32() > 0);
    }
    [Fact]
    public void RepeatedIdenticalToolCallsEmitOnlyOnePreview()
    {
        var context = new ToolExecutionContext(); var tool = new ProductionTools(context);
        tool.Execute(); tool.Execute(); Assert.Single(context.UiPayload);
        tool.Execute("Z9900001"); Assert.Equal(2, context.UiPayload.Count);
    }
    [Fact]
    public void UnscheduledOrderHasNoInventedDatesAndUnknownOrderHasNoMatches()
    {
        var tool = new ProductionTools(new());
        using var json = JsonDocument.Parse(tool.Execute("Z9900006").ToModelJson());
        var facts = json.RootElement.GetProperty("agentContext");
        Assert.Equal(1, facts.GetProperty("orderCount").GetInt32());
        Assert.Equal(0, facts.GetProperty("tasks").GetArrayLength());
        Assert.Equal(3, facts.GetProperty("pending").GetArrayLength());
        Assert.False(tool.Execute(lineId:"missing").Success);
        Assert.False(tool.Execute(date:"2026-02-30").Success);
        Assert.Contains("0 个未完工订单",tool.Execute("missing").Summary);
    }
}
