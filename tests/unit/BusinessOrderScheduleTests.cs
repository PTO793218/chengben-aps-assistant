using System.Text.Json;
using WePilot.Agent.Tools;
using Xunit;

namespace WePilot.Tests;

public sealed class BusinessOrderScheduleTests
{
    private static JsonElement Snapshot(params object[] tasks) => JsonSerializer.SerializeToElement(new
    {
        version = "V1", asOf = "2026-09-11T10:00:00+08:00", systemNo = "test",
        orders = new[] { new { orderId = "Z9900014", calcGuid = "d1" } }, tasks
    });
    private static object Task(string id, double quantity, string line = "L1", string cycle = "F", int mapping = 1, string date = "2026-09-16") => new
    {
        id, quantity, calcGuid = "d1", orderId = "Z9900014", productNo = "P1", productName = "test", operationNo = "A1", operation = "test",
        craftSequence = 1, bomLevel = "001", segmentNo = "S1", stage = "test", lineId = line, lineName = line, date, endDate = date,
        shift = "day", plannedHours = 1.25, changeHours = 0.5, schedulingTag = cycle, assistTag = "F", mappingCount = mapping
    };

    [Fact]
    public void RawQuantitiesArePreservedAndLinesAreNotMerged()
    {
        var result = BusinessOrderSchedule.Build(Snapshot(Task("p1", 2.5), Task("p2", 7, "L2", "T")), "Z9900014");
        Assert.True(result.Success);
        var spec = JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec);
        Assert.Equal(2, spec.GetProperty("chart").GetProperty("rows").GetArrayLength());
        Assert.Equal("7", spec.GetProperty("chart").GetProperty("items")[1].GetProperty("label").GetString());
        var model = result.ToModelJson();
        Assert.DoesNotContain("uiPayload", model);
        Assert.DoesNotContain("p1", model);
        Assert.DoesNotContain("plannedHours", model);
        Assert.DoesNotContain("riskCount", model);
    }

    [Fact]
    public void ZeroCycleRecordIsPreservedAsPlaceholder()
    {
        var result = BusinessOrderSchedule.Build(Snapshot(Task("p1", 0, cycle: "T")), "Z9900014");
        var spec = JsonSerializer.SerializeToElement(result.UiPayload.Single().Spec);
        Assert.Equal("周期", spec.GetProperty("chart").GetProperty("items")[0].GetProperty("label").GetString());
        Assert.Equal(0, spec.GetProperty("detail").GetProperty("rows")[0].GetProperty("quantity").GetDouble());
    }

    [Fact]
    public void InvalidVersionAndAmbiguousMappingDoNotGuessOrFallback()
    {
        Assert.False(BusinessOrderSchedule.Build(Snapshot(Task("p1", 10)), "Z9900014", "other").Success);
        Assert.False(BusinessOrderSchedule.Build(Snapshot(Task("p1", 10)), "unknown").Success);
        Assert.False(BusinessOrderSchedule.Build(Snapshot(Task("p1", 10, mapping: 2)), "Z9900014").Success);
        Assert.False(BusinessOrderSchedule.Build(Snapshot(Task("p1", 10)), "Z9900014", startDate: "2026-01-01", endDate: "2026-12-31").Success);
    }

    [Fact]
    public void ExplicitRangeOnlyChangesIncludedRecords()
    {
        var result = BusinessOrderSchedule.Build(Snapshot(Task("p1", 2.5), Task("p2", 7, date: "2026-09-17")), "Z9900014", startDate: "2026-09-17", endDate: "2026-09-17");
        Assert.Contains("1 条计划记录", result.Summary);
        var empty = BusinessOrderSchedule.Build(Snapshot(Task("p1", 10)), "Z9900014", startDate: "2026-10-01", endDate: "2026-10-02");
        Assert.True(empty.Success);
        Assert.Contains("不据此判断", empty.Summary);
    }
}
