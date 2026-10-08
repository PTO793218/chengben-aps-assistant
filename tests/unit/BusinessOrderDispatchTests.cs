using System.Text.Json;
using WePilot.Agent.Tools;
using Xunit;

namespace WePilot.Tests;

public sealed class BusinessOrderDispatchTests
{
    [Fact]
    public void DerivesOrderStatesAndExcludesOrphanTasksFromOrderCount()
    {
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            source = "synthetic-demo",
            snapshotId = "TEST-ORDER-DISPATCH",
            systemNo = "731",
            asOf = "2026-09-21T17:30:00+08:00",
            orders = new[]
            {
                new { orderNo = "O1", pdPlanGuid = "P1", productNo = "A", productName = "产品A", lineId = "L1", quantity = 100, planStart = "2026-09-01", planEnd = "2026-09-01" },
                new { orderNo = "O2", pdPlanGuid = "P2", productNo = "B", productName = "产品B", lineId = "L1", quantity = 100, planStart = "2026-09-01", planEnd = "2026-09-01" },
                new { orderNo = "O3", pdPlanGuid = "P3", productNo = "C", productName = "产品C", lineId = "L2", quantity = 100, planStart = "2026-09-01", planEnd = "2026-09-01" },
                new { orderNo = "O4", pdPlanGuid = "P4", productNo = "D", productName = "产品D", lineId = "L2", quantity = 100, planStart = "2026-09-01", planEnd = "2026-09-01" }
            },
            plans = new[]
            {
                new { planGuid = "P1", planNo = "O1", productNo = "A", productName = "产品A", lineId = "L1", planStart = "2026-09-01", planEnd = "2026-09-01", planQuantity = 100 },
                new { planGuid = "P2", planNo = "O2", productNo = "B", productName = "产品B", lineId = "L1", planStart = "2026-09-01", planEnd = "2026-09-01", planQuantity = 100 },
                new { planGuid = "P3", planNo = "O3", productNo = "C", productName = "产品C", lineId = "L2", planStart = "2026-09-01", planEnd = "2026-09-01", planQuantity = 100 },
                new { planGuid = "P4", planNo = "O4", productNo = "D", productName = "产品D", lineId = "L2", planStart = "2026-09-01", planEnd = "2026-09-01", planQuantity = 100 }
            },
            dispatchTasks = new[]
            {
                new { taskGuid = "T1", pdPlanGuid = "P1", taskNo = "T1", productNo = "A", productName = "产品A", lineId = "L1", taskStart = "2026-09-01", taskEnd = "2026-09-01", taskQuantity = 100, deliveryQuantity = 20, status = "run" },
                new { taskGuid = "T2", pdPlanGuid = "P2", taskNo = "T2", productNo = "B", productName = "产品B", lineId = "L1", taskStart = "2026-09-01", taskEnd = "2026-09-01", taskQuantity = 100, deliveryQuantity = 100, status = "finish" },
                new { taskGuid = "T3", pdPlanGuid = "P3", taskNo = "T3", productNo = "C", productName = "产品C", lineId = "L2", taskStart = "2026-09-01", taskEnd = "2026-09-01", taskQuantity = 100, deliveryQuantity = 100, status = "close" },
                new { taskGuid = "ORPHAN", pdPlanGuid = "PX", taskNo = "ORPHAN", productNo = "X", productName = "未知产品", lineId = "L1", taskStart = "2026-09-01", taskEnd = "2026-09-01", taskQuantity = 100, deliveryQuantity = 20, status = "run" }
            },
            latestTasks = new[]
            {
                new { taskGuid = "H4", pdPlanGuid = "P4", taskNo = "H4", productNo = "D", productName = "产品D", lineId = "L2", taskStart = "2026-08-31", taskEnd = "2026-08-31", taskQuantity = 100, deliveryQuantity = 100, status = "finish" }
            }
        }));

        var result = BusinessOrderDispatch.Build(snapshot.RootElement);
        Assert.True(result.Success);

        var context = JsonSerializer.SerializeToElement(result.AgentContext);
        Assert.Equal(4, context.GetProperty("orderCount").GetInt32());
        Assert.Equal(1, context.GetProperty("dispatchInProgressOrderCount").GetInt32());
        Assert.Equal(1, context.GetProperty("completedOrderCount").GetInt32());
        Assert.Equal(1, context.GetProperty("closedOrderCount").GetInt32());
        Assert.Equal(1, context.GetProperty("taskNotGeneratedOrderCount").GetInt32());
        Assert.Equal(1, context.GetProperty("unmatchedTaskCount").GetInt32());

        var block = Assert.Single(result.UiPayload);
        Assert.Equal("order-dispatch", block.Type);
        var spec = JsonSerializer.SerializeToElement(block.Spec);
        Assert.Equal(6, spec.GetProperty("metrics").GetArrayLength());
        Assert.Equal(1, spec.GetProperty("detailGroups").GetProperty("unmatchedTask").GetArrayLength());
        var limitations = string.Join(" ", spec.GetProperty("limitations").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("当前快照没有现场下发或流转凭证", limitations);
        Assert.Equal("当前快照未取得（不代表未下发）", spec.GetProperty("releaseStatusNote").GetString());
        Assert.DoesNotContain("taskTag", limitations);

        var focused = BusinessOrderDispatch.Build(snapshot.RootElement, orderId: "O1");
        Assert.True(focused.Success);
        Assert.Contains("O1", focused.Summary);
        var focusedContext = JsonSerializer.SerializeToElement(focused.AgentContext);
        Assert.Equal(1, focusedContext.GetProperty("orderCount").GetInt32());
        Assert.Equal(1, focusedContext.GetProperty("currentTaskCount").GetInt32());
        Assert.Equal(0, focusedContext.GetProperty("unmatchedTaskCount").GetInt32());
        var focusedSpec = JsonSerializer.SerializeToElement(focused.UiPayload[0].Spec);
        Assert.Equal("O1", focusedSpec.GetProperty("orderRows")[0].GetProperty("orderNo").GetString());
        Assert.Equal("O1", focusedSpec.GetProperty("taskRows")[0].GetProperty("orderNo").GetString());

        var noCurrentTask = BusinessOrderDispatch.Build(snapshot.RootElement, orderId: "O4");
        Assert.True(noCurrentTask.Success);
        Assert.Contains("未找到当前任务", noCurrentTask.Summary);
        Assert.Equal(0, JsonSerializer.SerializeToElement(noCurrentTask.AgentContext).GetProperty("currentTaskCount").GetInt32());
        var missing = BusinessOrderDispatch.Build(snapshot.RootElement, orderId: "MISSING");
        Assert.False(missing.Success);
        Assert.Contains("当前快照", missing.Summary);
    }

    [Fact]
    public void RunningTaskWithNoRemainingQuantityDoesNotBecomeInProgress()
    {
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            source = "synthetic-demo",
            orders = new[] { new { orderNo = "O1", pdPlanGuid = "P1", productNo = "A", productName = "产品A", lineId = "L1", quantity = 100, planStart = "2026-09-01", planEnd = "2026-09-01" } },
            plans = new[] { new { planGuid = "P1", planNo = "O1", productNo = "A", productName = "产品A", lineId = "L1", planStart = "2026-09-01", planEnd = "2026-09-01", planQuantity = 100 } },
            dispatchTasks = new[] { new { taskGuid = "T1", pdPlanGuid = "P1", taskNo = "T1", productNo = "A", productName = "产品A", lineId = "L1", taskStart = "2026-09-01", taskEnd = "2026-09-01", taskQuantity = 100, deliveryQuantity = 100, status = "run" } },
            latestTasks = Array.Empty<object>()
        }));

        var result = BusinessOrderDispatch.Build(snapshot.RootElement);
        Assert.True(result.Success);
        var context = JsonSerializer.SerializeToElement(result.AgentContext);
        Assert.Equal(0, context.GetProperty("dispatchInProgressOrderCount").GetInt32());
        Assert.Equal(1, context.GetProperty("unknownOrderCount").GetInt32());
    }

    [Fact]
    public void OrderWithoutPlanDoesNotClaimAnExistingPlan()
    {
        using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            source = "synthetic-demo",
            orders = new[] { new { orderNo = "O1", pdPlanGuid = "P1", productNo = "A", productName = "产品A", lineId = "", quantity = 100, planStart = "", planEnd = "" } },
            plans = Array.Empty<object>(),
            dispatchTasks = Array.Empty<object>(),
            latestTasks = Array.Empty<object>()
        }));
        var result = BusinessOrderDispatch.Build(snapshot.RootElement, orderId: "O1");
        Assert.True(result.Success);
        var spec = JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec);
        var reason = spec.GetProperty("orderRows")[0].GetProperty("reason").GetString();
        Assert.Contains("未排入计划", reason);
        Assert.DoesNotContain("有计划但", reason);
    }
}
