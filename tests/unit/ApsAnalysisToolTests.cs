using System.Text.Json;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;

public sealed class ApsAnalysisToolTests
{
    [Fact]
    public void OverviewKeepsChartDataOutOfModelContext()
    {
        var context = new ToolExecutionContext();
        var result = new ApsAnalysisTools(context).Execute("overview");

        Assert.True(result.Success);
        Assert.Single(result.UiPayload);
        Assert.Equal("aps-analysis", result.UiPayload[0].Type);
        using var model = JsonDocument.Parse(result.ToModelJson());
        Assert.False(model.RootElement.TryGetProperty("uiPayload", out _));
        Assert.False(model.RootElement.GetProperty("agentContext").TryGetProperty("tasks", out _));
        Assert.Equal(12, model.RootElement.GetProperty("agentContext").GetProperty("orderCount").GetInt32());
    }

    [Fact]
    public void ConfirmedQuestionTypesReturnFixedGanttViews()
    {
        var tool = new ApsAnalysisTools(new());
        var risk = tool.Execute("risk");
        var order = tool.Execute("order", "Z9900006");

        Assert.True(risk.Success);
        Assert.True(order.Success);
        var completed = tool.Execute("order", "Z9900001");
        Assert.True(completed.Success);
        Assert.Contains("2026-09-01", JsonSerializer.Serialize(completed.AgentContext));
        var future = tool.Execute("order", "Z9900005");
        Assert.True(future.Success);
        Assert.Contains("2026-10-13", JsonSerializer.Serialize(future.AgentContext));
        Assert.DoesNotContain("范围内有 0 条", future.Summary);
        using var orderUi = JsonDocument.Parse(JsonSerializer.Serialize(order.UiPayload[0].Spec));
        Assert.Equal("gantt", orderUi.RootElement.GetProperty("chart").GetProperty("kind").GetString());
        Assert.False(tool.Execute("pending").Success);
        Assert.False(tool.Execute("order", "missing").Success);
        Assert.False(tool.Execute("overview", startDate: "2026-10-01", endDate: "2026-09-01").Success);
    }

    [Fact]
    public void IdenticalCallsEmitOneUiBlock()
    {
        var context = new ToolExecutionContext();
        var tool = new ApsAnalysisTools(context);
        tool.Execute("overview");
        tool.Execute("overview");
        tool.Execute("risk");
        Assert.Equal(2, context.UiPayload.Count);
    }

    [Fact]
    public void RemovedIncompleteSceneRejectsItsOldScopeWithoutEmittingAView()
    {
        var context = new ToolExecutionContext();
        var tool = new ApsAnalysisTools(context);
        tool.Execute("overview");
        var result = tool.Execute("overview", scheduleScope: "incomplete");

        Assert.False(result.Success);
        Assert.Single(context.UiPayload);
        Assert.Empty(result.UiPayload);
        Assert.Contains("移除", result.Summary);
        Assert.False(tool.Execute("overview", scheduleScope: "unknown").Success);
    }
}
