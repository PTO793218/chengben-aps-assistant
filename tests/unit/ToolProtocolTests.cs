using System.Text.Json;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;
namespace WePilot.Tests;
public sealed class ToolProtocolTests
{
    [Theory]
    [InlineData("bar")][InlineData("line")][InlineData("pie")]
    public void ToolResultSeparatesModelFactsFromUi(string type)
    {
        var context = new ToolExecutionContext();
        var result = new SampleTools(context).Execute(type);
        using var model = JsonDocument.Parse(result.ToModelJson());
        Assert.True(model.RootElement.GetProperty("success").GetBoolean());
        Assert.False(model.RootElement.TryGetProperty("uiPayload", out _));
        Assert.Equal(320, model.RootElement.GetProperty("agentContext").GetProperty("total").GetInt32());
        var chart = Assert.IsType<ChartSpec>(Assert.Single(result.UiPayload).Spec);
        Assert.Equal(type, chart.ChartType);
        Assert.Equal(320, chart.Data.Sum(row => Convert.ToInt32(row[chart.YField])));
        Assert.Single(context.ToolCalls);
        Assert.Single(context.UiPayload);
    }
    [Fact]
    public void InvalidArgumentsProduceNoChart()
    {
        var context = new ToolExecutionContext();
        var result = new SampleTools(context).Execute("script");
        Assert.False(result.Success);Assert.Empty(result.UiPayload);Assert.Empty(context.UiPayload);
    }
    [Fact]
    public void RequestContextsDoNotShareResults()
    {
        var first = new ToolExecutionContext();var second = new ToolExecutionContext();
        new SampleTools(first).Execute("bar");Assert.Empty(second.ToolCalls);Assert.Empty(second.UiPayload);
    }
    [Fact]
    public void OrdinaryToolIsRegisteredWithExpectedParameter()
    {
        var kernel=Kernel.CreateBuilder().Build();
        kernel.Plugins.AddFromObject(new SampleTools(new()),"tools");
        Assert.True(kernel.Plugins.TryGetFunction("tools","get_sample_data",out var function));
        Assert.Contains(function!.Metadata.Parameters,p=>p.Name=="chartType");
    }
}
