using System.Text.Json;
using WePilot.Agent.Tools;
using Xunit;

namespace WePilot.Tests;

public sealed class BusinessMaterialReadinessTests
{
    [Fact]
    public void PayloadReconcilesThirteenLinkedOrdersAndCarriesRiskEvidence()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var path = Path.Combine(projectRoot, "agent-backend", "Data", "production-demo.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var snapshot = document.RootElement.GetProperty("materialReadiness");

        var result = BusinessMaterialReadiness.Build(snapshot);
        Assert.True(result.Success);

        var spec = JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec);
        var summary = spec.GetProperty("model").GetProperty("summary");
        Assert.Equal(13, summary.GetProperty("totalOrders").GetInt32());
        Assert.Equal(8, summary.GetProperty("readyOrders").GetInt32());
        Assert.Equal(4, summary.GetProperty("notObtainedOrders").GetInt32());
        Assert.Equal(1, summary.GetProperty("unknownOrders").GetInt32());

        var rows = spec.GetProperty("model").GetProperty("rows").EnumerateArray().ToArray();
        Assert.Contains(rows, row =>
            row.GetProperty("risk").GetProperty("scenarioId").GetString() == "S03-stock-occupied");
        Assert.Contains(rows.SelectMany(row => row.GetProperty("materials").EnumerateArray()), cell =>
            cell.TryGetProperty("evidence", out var evidence)
            && evidence.ValueKind == JsonValueKind.Object
            && evidence.GetProperty("scenarioId").GetString() == "S04-supply-late"
            && evidence.GetProperty("riskCode").GetString() == "supply-late");

        var focused = BusinessMaterialReadiness.Build(snapshot, scope: "order", orderId: "Z9900001");
        Assert.True(focused.Success);
        Assert.Contains("2 条物料结果", focused.Summary);
        var focusedContext = JsonSerializer.SerializeToElement(focused.AgentContext);
        Assert.Equal(1, focusedContext.GetProperty("totalOrders").GetInt32());
        Assert.Equal(2, focusedContext.GetProperty("readinessResultRowCount").GetInt32());
    }
}
