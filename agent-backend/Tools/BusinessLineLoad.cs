using System.Text.Json;

namespace WePilot.Agent.Tools;

// All arithmetic stays outside the model. Missing calendar days are never replaced with zero/8/24 hours.
public static class BusinessLineLoad
{
    public sealed record Period(string Grain, string PeriodKey, string LineId, string LineName,
        double? CapacityHours, double? MadeHours, double? ChangeHours, double? OrderMadeHours,
        double? OrderChangeHours, double? PlannedHours, double? OrderHours, double? LoadPercent,
        double? RemainingHours, int MissingCalendarDays, int InvalidCalendarDays, int NonworkingDays,
        int OverloadedDays, int ZeroCapacityPlanDays, int MissingPlanHours, int RecordCount);
    public sealed record Result(bool Available, string DefaultPeriod, string Note, string Summary, Period[] Periods);

    public static Result Build(JsonElement snapshot, string orderId, JsonElement[] orderTasks)
    {
        const string note = "负载=(生产工时+换型工时)/理论工时。理论工时按产线工作日历的班次工时×产能系数折算；停工日为0，缺失日历不补值。整线包含同系统同版本全部计划，本单是其中一部分。工时按计划开始日归属，非实际报工；日历为当前导出设置，不是历史版本日历。";
        if (!snapshot.TryGetProperty("loadTasks", out var load) || !snapshot.TryGetProperty("calendar", out var calendar) || orderTasks.Length == 0)
            return new(false, "", note, "产线负载数据未提供。", Array.Empty<Period>());
        var first = DateOnly.Parse(Text(orderTasks.OrderBy(x => Text(x, "date")).First(), "date"));
        var last = DateOnly.Parse(orderTasks.Max(x => Text(x, "date"))!);
        var start = new DateOnly(first.Year, 1, 1);
        var end = new DateOnly(last.Year, 12, 31);
        var lines = orderTasks.GroupBy(x => Text(x, "lineId")).ToDictionary(g => g.Key, g => Text(g.First(), "lineName"));
        var planLookup = load.EnumerateArray().Where(x => lines.ContainsKey(Text(x, "lineId")))
            .ToLookup(x => (Text(x, "lineId"), Text(x, "date")));
        var calendarLookup = calendar.EnumerateArray().Where(x => lines.ContainsKey(Text(x, "lineId")))
            .ToLookup(x => (Text(x, "lineId"), Text(x, "date")));
        var days = new List<Period>();
        foreach (var line in lines)
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var key = date.ToString("yyyy-MM-dd");
            var c = calendarLookup[(line.Key, key)].ToArray();
            var p = planLookup[(line.Key, key)].ToArray();
            var own = p.Where(x => Text(x, "orderId") == orderId).ToArray();
            double? capacity = c.Length == 1 ? CalendarHours(c[0]) : null;
            var made = Sum(p.Select(x => Number(x, "plannedHours")));
            var change = Sum(p.Select(x => Number(x, "changeHours")));
            var ownMade = Sum(own.Select(x => Number(x, "plannedHours")));
            var ownChange = Sum(own.Select(x => Number(x, "changeHours")));
            var planned = made + change;
            days.Add(new("day", key, line.Key, line.Value, capacity, made, change, ownMade, ownChange,
                planned, ownMade + ownChange, capacity > 0 ? planned / capacity * 100 : null,
                capacity - planned, c.Length == 0 ? 1 : 0, c.Length > 0 && capacity == null ? 1 : 0,
                capacity == 0 ? 1 : 0, capacity > 0 && planned > capacity + 0.000001 ? 1 : 0,
                capacity == 0 && planned > 0 ? 1 : 0,
                p.Count(x => Number(x, "plannedHours") == null || Number(x, "changeHours") == null), p.Length));
        }
        var periods = days.Concat(Aggregate(days, "month", 7)).Concat(Aggregate(days, "year", 4)).ToArray();
        var month = first.ToString("yyyy-MM");
        var initial = periods.Where(p => p.Grain == "month" && p.PeriodKey == month).ToArray();
        var summary = $"已附{lines.Count}条相关产线负载，默认{month}整月（与甘特筛选范围独立）；" +
            $"该月超载日合计{initial.Sum(x => x.OverloadedDays)}个产线日，停工但有排程{initial.Sum(x => x.ZeroCapacityPlanDays)}个产线日，缺失/无效日历{initial.Sum(x => x.MissingCalendarDays + x.InvalidCalendarDays)}个产线日。仅反映同版本已排工时，非报工。";
        return new(true, month, note, summary, periods);
    }

    private static IEnumerable<Period> Aggregate(IEnumerable<Period> days, string grain, int length)
    {
        foreach (var g in days.GroupBy(x => (x.LineId, Key: x.PeriodKey[..length])))
        {
            var capacity = Sum(g.Select(x => x.CapacityHours));
            var made = Sum(g.Select(x => x.MadeHours));
            var change = Sum(g.Select(x => x.ChangeHours));
            var ownMade = Sum(g.Select(x => x.OrderMadeHours));
            var ownChange = Sum(g.Select(x => x.OrderChangeHours));
            var planned = made + change;
            yield return new(grain, g.Key.Key, g.Key.LineId, g.First().LineName,
                capacity, made, change, ownMade, ownChange, planned, ownMade + ownChange,
                capacity > 0 ? planned / capacity * 100 : null, capacity - planned,
                g.Sum(x => x.MissingCalendarDays), g.Sum(x => x.InvalidCalendarDays), g.Sum(x => x.NonworkingDays),
                g.Sum(x => x.OverloadedDays), g.Sum(x => x.ZeroCapacityPlanDays), g.Sum(x => x.MissingPlanHours), g.Sum(x => x.RecordCount));
        }
    }
    private static double? CalendarHours(JsonElement c)
    {
        if (Text(c, "holiday") == "T") return 0;
        if (Text(c, "holiday") != "F") return null;
        double total = 0;
        foreach (var n in new[] { 1, 2 })
        {
            var shift = Text(c, $"shift{n}");
            var hours = Number(c, $"hours{n}");
            if (shift == "" && hours == 0) continue;
            var rate = Number(c, $"rate{n}");
            if (shift is not ("02" or "03") || hours == null || rate == null) return null;
            total += hours.Value * rate.Value / 100;
        }
        return total;
    }
    private static double? Sum(IEnumerable<double?> values)
    {
        var items = values.ToArray();
        return items.Any(x => x == null) ? null : items.Sum(x => x!.Value);
    }
    private static string Text(JsonElement x, string key) => x.GetProperty(key).GetString() ?? "";
    private static double? Number(JsonElement x, string key) => x.TryGetProperty(key, out var v) && v.TryGetDoubleSafe(out var d) && double.IsFinite(d) && d >= 0 ? d : null;
    private static bool TryGetDoubleSafe(this JsonElement x, out double value)
    {
        value = 0;
        return x.ValueKind == JsonValueKind.Number && x.TryGetDouble(out value);
    }
}
