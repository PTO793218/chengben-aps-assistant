using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

public sealed class ApsAnalysisTools
{
    private readonly ToolExecutionContext _context;
    private readonly HashSet<string> _emitted = new();
    private static readonly JsonElement Snapshot = LoadSnapshot();
    private readonly BusinessOrderSchedule? _business;

    public ApsAnalysisTools(ToolExecutionContext context, BusinessOrderSchedule? business = null) => (_context, _business) = (context, business);

    [KernelFunction("get_plan_overview")]
    [Description("查看未完工订单的排程总览。返回简短事实给AI，并把完整指标、二维甘特图和订单明细直接交给前端渲染。只读，不重排。")]
    public string GetPlanOverview(
        [Description("可选开始日期 YYYY-MM-DD；空值使用示例默认范围")] string startDate = "",
        [Description("可选结束日期 YYYY-MM-DD；空值使用示例默认范围")] string endDate = "",
        [Description("all=全部未完工订单；incomplete=仅部分已排或尚未排入的订单，不推断原因")] string scheduleScope = "all")
        => Execute("overview", "", startDate, endDate, scheduleScope: scheduleScope).ToModelJson();

    [KernelFunction("get_delivery_risks")]
    [Description("查看计划交期风险订单。返回风险结论，并把风险订单甘特图和交期明细直接交给前端。只读，不承诺交付。")]
    public string GetDeliveryRisks(
        [Description("可选开始日期 YYYY-MM-DD；空值使用示例默认范围")] string startDate = "",
        [Description("可选结束日期 YYYY-MM-DD；空值使用示例默认范围")] string endDate = "")
        => Execute("risk", "", startDate, endDate).ToModelJson();

    [KernelFunction("get_order_schedule")]
    [Description("查看一个订单已有的排程记录、工段、日期、班次和产线，并附相关产线整线负载、本单占用、工作日历理论工时，界面支持年/月/日。默认真实业务快照；仅明确要合成演示才传source=demo。不判断齐套、完工或排入完整性。")]
    public string GetOrderSchedule(
        [Description("完整订单号，例如 Z9900001")] string orderId,
        [Description("可选开始日期 YYYY-MM-DD；真实查询空值取订单最早计划日期")] string startDate = "",
        [Description("可选结束日期 YYYY-MM-DD；真实查询空值取订单最晚计划日期")] string endDate = "",
        [Description("可选需求版本号；空值使用已加载快照版本，不存在的版本报错")] string version = "",
        [Description("business=真实快照，demo=明确请求合成演示；空值在配置真实快照时使用business")] string source = "")
        => Execute("order", orderId, startDate, endDate, version, source).ToModelJson();

    public ToolResult Execute(string viewType, string orderId = "", string startDate = "", string endDate = "", string version = "", string source = "", string scheduleScope = "all")
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = ExecuteCore(viewType, orderId, startDate, endDate, version, source, scheduleScope);
        _context.ToolCalls.Add(new()
        {
            ToolName = viewType.Trim().ToLowerInvariant() switch { "order" => "get_order_schedule", "risk" => "get_delivery_risks", _ => "get_plan_overview" },
            Arguments = new { orderId, startDate, endDate, version, source, scheduleScope },
            Success = result.Success, Summary = result.Summary, ElapsedMs = (int)watch.ElapsedMilliseconds
        });
        return result;
    }

    private ToolResult ExecuteCore(string viewType, string orderId, string startDate, string endDate, string version, string source, string scheduleScope)
    {
        viewType = (viewType ?? "").Trim().ToLowerInvariant();
        orderId = (orderId ?? "").Trim().ToUpperInvariant();
        scheduleScope = (scheduleScope ?? "all").Trim().ToLowerInvariant();
        if (viewType is not ("overview" or "risk" or "order"))
            return Fail("不支持的分析类型。", "viewType 使用 overview/risk/order。");
        if (scheduleScope is not ("all" or "incomplete") || viewType != "overview" && scheduleScope != "all")
            return Fail("不支持的排入状态筛选。", "生产计划总览的 scheduleScope 使用 all 或 incomplete。");
        if (viewType == "order" && orderId.Length == 0)
            return Fail("查看单订单排程需要订单号。", "请提供完整订单号，例如 Z9900001。");
        source = (source ?? "").Trim().ToLowerInvariant();
        if (source is not ("" or "business" or "demo")) return Fail("数据来源无效。", "source使用business或demo。");
        if (source == "business" && viewType != "order") return Fail("真实总览与风险查询尚未接入。", "当前仅支持真实单订单查询。");
        if (viewType == "order" && (source == "business" || source == "" && _business?.IsConfigured == true))
        {
            var real = _business?.Execute(orderId, version.Trim(), startDate, endDate)
                ?? Fail("真实订单快照尚未配置。", "未回退到合成示例。");
            if (_emitted.Add($"business|{orderId}|{version}|{startDate}|{endDate}")) _context.UiPayload.AddRange(real.UiPayload);
            return real;
        }
        if (version.Length > 0) return Fail("合成示例不支持业务版本筛选。", "请查询真实订单快照。");

        var defaultStart = Snapshot.GetProperty("defaultStart").GetString()!;
        var defaultEnd = Snapshot.GetProperty("defaultEnd").GetString()!;
        if (viewType == "order" && orderId.Length > 0 && startDate.Length == 0 && endDate.Length == 0)
        {
            var orderTaskDates = Snapshot.GetProperty("tasks").EnumerateArray()
                .Where(task => Text(task, "orderId") == orderId)
                .Select(task => Text(task, "date"))
                .Where(date => date.Length > 0)
                .OrderBy(date => date, StringComparer.Ordinal)
                .ToArray();
            if (orderTaskDates.Length > 0)
            {
                defaultStart = orderTaskDates[0];
                defaultEnd = orderTaskDates[^1];
            }
        }
        if (!TryRange(startDate, endDate, defaultStart, defaultEnd, out var start, out var end, out var rangeError))
            return Fail("日期范围无效。", rangeError);

        var allOrders = Snapshot.GetProperty("orders").EnumerateArray()
            .Where(x => viewType == "order" || x.GetProperty("productionStatus").GetString() != "completed").ToArray();
        var allTasks = Snapshot.GetProperty("tasks").EnumerateArray().ToArray();
        var orders = allOrders.Where(x =>
            (orderId.Length == 0 || Text(x, "id") == orderId) &&
            (viewType != "risk" || x.GetProperty("risk").GetBoolean()) &&
            (scheduleScope != "incomplete" || (Text(x, "scheduleStatus") is "partial" or "none"))).ToArray();
        if (orders.Length == 0)
            return Fail($"示例快照中没有匹配的{(viewType == "risk" ? "风险订单" : viewType == "order" ? "订单" : "未完工订单")}。", "请核对订单号或筛选范围。");

        var ids = orders.Select(x => Text(x, "id")).ToHashSet();
        var tasks = allTasks.Where(x => ids.Contains(Text(x, "orderId")) &&
            DateOnly.Parse(Text(x, "date"), CultureInfo.InvariantCulture) >= start &&
            DateOnly.Parse(Text(x, "date"), CultureInfo.InvariantCulture) <= end).ToArray();
        var full = orders.Count(x => Text(x, "scheduleStatus") == "full");
        var partial = orders.Count(x => Text(x, "scheduleStatus") == "partial");
        var none = orders.Length - full - partial;
        var risk = orders.Count(x => x.GetProperty("risk").GetBoolean());

        var summary = viewType switch
        {
            "risk" => $"示例中有 {orders.Length} 个计划交期风险订单；其中 {full} 个已排完整、{partial} 个部分已排、{none} 个尚未排入。风险标记不等于必然延期。",
            "order" => $"订单 {orderId} 当前排入状态为{ScheduleLabel(Text(orders[0], "scheduleStatus"))}，范围内有 {tasks.Length} 条班次计划记录，涉及 {tasks.Select(x => Text(x, "stage")).Distinct().Count()} 个工段，计划交期风险为{(orders[0].GetProperty("risk").GetBoolean() ? "是" : "否")}。",
            "overview" when scheduleScope == "incomplete" => $"示例中有 {orders.Length} 个未完整排入计划的订单：{string.Join("、", orders.Select(x => Text(x, "id")))}；其中 {partial} 个部分已排、{none} 个尚未排入。当前演示数据没有可核对的未排原因。",
            _ => $"示例中有 {orders.Length} 个未完工订单：{full} 个已排完整、{partial} 个部分已排、{none} 个尚未排入；{risk} 个有计划交期风险。"
        };

        var metrics = viewType == "order"
            ? new object[] { Metric("班次计划", tasks.Length, "条"), Metric("涉及工段", tasks.Select(x => Text(x, "stage")).Distinct().Count(), "个"), Metric("计划日期", tasks.Select(x => Text(x, "date")).Distinct().Count(), "天"), Metric("涉及产线", tasks.Select(x => Text(x, "lineId")).Distinct().Count(), "条") }
            : scheduleScope == "incomplete"
            ? new object[] { Metric("未完整排入订单", orders.Length, "个"), Metric("部分已排", partial, "个"), Metric("尚未排入", none, "个"), Metric("交期风险", risk, "个") }
            : new object[] { Metric("未完工订单", orders.Length, "个"), Metric("完整排入", full, "个"), Metric("部分排入", partial, "个"), Metric("交期风险", risk, "个") };

        var chart = BuildGantt(tasks, orders, start, end);
        var details = BuildOrderDetails(orders);
        var title = viewType switch { "risk" => "计划交期风险", "order" => $"订单 {orderId} 排程", _ when scheduleScope == "incomplete" => "未完整排入计划的订单", _ => "生产计划总览" };
        var result = new ToolResult
        {
            Success = true,
            Summary = summary,
            AgentContext = new
            {
                source = "synthetic-demo",
                snapshotId = Text(Snapshot, "id"),
                asOf = Text(Snapshot, "asOf"),
                viewType,
                orderId,
                scheduleScope,
                range = new { start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") },
                orderCount = orders.Length,
                fullyScheduled = full,
                partiallyScheduled = partial,
                unscheduled = none,
                riskCount = risk,
                scheduledTaskCount = tasks.Length,
                focusOrderIds = scheduleScope == "incomplete" ? orders.Select(x => Text(x, "id")).ToArray() : Array.Empty<string>(),
                attentionOrderIds = orders.Where(x => x.GetProperty("risk").GetBoolean()).Select(x => Text(x, "id")).Take(5).ToArray(),
                limitations = "合成示例、非实时业务。已排完整不等于生产完成或按期交付；不执行优化、改排或下发。"
            },
            UiPayload = new()
            {
                new UiBlock
                {
                    Type = "aps-analysis",
                    Title = title,
                    Spec = new
                    {
                        viewType,
                        scheduleScope,
                        source = "synthetic-demo",
                        eyebrow = "APS READ-ONLY ANALYSIS",
                        sourceLabel = "本地合成示例 · 非实时业务",
                        snapshotId = Text(Snapshot, "id"),
                        asOf = Text(Snapshot, "asOf"),
                        range = new { start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") },
                        headline = summary,
                        metrics,
                        chart,
                        detail = details,
                        limitations = new[] { "图表数据由业务工具按固定规则计算，AI 不生成图表明细。", "任务数量不可跨工段累加为成品产量。", "当前仅查看、判断与对比，不执行优化、改排或写入。" }
                    }
                }
            }
        };
        Record(viewType, orderId, start, end, scheduleScope, result);
        return result;
    }

    private void Record(string viewType, string orderId, DateOnly start, DateOnly end, string scheduleScope, ToolResult result)
    {
        var key = $"{viewType}|{orderId}|{start:yyyy-MM-dd}|{end:yyyy-MM-dd}|{scheduleScope}";
        if (_emitted.Add(key)) _context.UiPayload.AddRange(result.UiPayload);
    }

    private static object BuildGantt(JsonElement[] tasks, JsonElement[] orders, DateOnly start, DateOnly end)
    {
        var columns = new List<object>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var weekday = WeekdayLabel(date.DayOfWeek);
            columns.Add(new { key = $"{date:yyyy-MM-dd}-D", date = date.ToString("yyyy-MM-dd"), weekday, shift = "白班", label = $"{date:MM-dd} 白" });
            columns.Add(new { key = $"{date:yyyy-MM-dd}-N", date = date.ToString("yyyy-MM-dd"), weekday, shift = "晚班", label = $"{date:MM-dd} 晚" });
        }
        var orderMap = orders.ToDictionary(x => Text(x, "id"));
        var lineMap = Snapshot.GetProperty("lines").EnumerateArray().ToDictionary(x => Text(x, "id"), x => Text(x, "name"));
        var rowKeys = tasks.Select(x => $"{Text(x, "orderId")}|{Text(x, "stage")}").Distinct().ToArray();
        var rowIndexes = rowKeys.Select((key, index) => (key, index)).ToDictionary(x => x.key, x => x.index);
        var rows = rowKeys.Select(key =>
        {
            var parts = key.Split('|');
            var order = orderMap[parts[0]];
            var rowTasks = tasks.Where(x => Text(x, "orderId") == parts[0] && Text(x, "stage") == parts[1]).ToArray();
            return new
            {
                key,
                lineName = string.Join(" / ", rowTasks.Select(x => Text(x, "lineId")).Distinct().Select(x => lineMap.GetValueOrDefault(x, x))),
                productName = Text(order, "product"),
                productSpec = Text(order, "spec"),
                stage = parts[1],
                planQuantity = order.GetProperty("quantity").GetInt32(),
                orderId = parts[0],
                planStart = rowTasks.Min(x => Text(x, "date")),
                planEnd = rowTasks.Max(x => Text(x, "date"))
            };
        }).ToArray();
        var items = tasks.Select(x =>
        {
            var date = DateOnly.Parse(Text(x, "date"), CultureInfo.InvariantCulture);
            var columnIndex = date.DayNumber - start.DayNumber;
            columnIndex = columnIndex * 2 + (Text(x, "shift") == "晚班" ? 1 : 0);
            var id = Text(x, "orderId");
            return new
            {
                id = Text(x, "id"), rowIndex = rowIndexes[$"{id}|{Text(x, "stage")}"], columnIndex,
                orderId = id, stage = Text(x, "stage"), operation = Text(x, "operation"), date = Text(x, "date"),
                shift = Text(x, "shift"), lineId = Text(x, "lineId"), quantity = x.GetProperty("quantity").GetInt32(),
                plannedHours = x.GetProperty("plannedHours").GetDouble(), tone = Text(x, "shift") == "晚班" ? "night" : "day"
            };
        }).ToArray();
        return new { kind = "gantt", title = "排程甘特表", columns, rows, items, emptyText = "当前范围没有已排任务。" };
    }

    private static object BuildOrderDetails(JsonElement[] orders)
    {
        return new
        {
            title = "订单判断明细",
            columns = new[]
            {
                new { key = "orderId", label = "订单" }, new { key = "product", label = "产品" },
                new { key = "quantity", label = "数量" }, new { key = "scheduleStatus", label = "排入状态" },
                new { key = "dueDate", label = "交期" }, new { key = "plannedEnd", label = "计划结束" },
                new { key = "risk", label = "风险" }
            },
            rows = orders.Select(x => new
            {
                orderId = Text(x, "id"), product = Text(x, "product"), quantity = x.GetProperty("quantity").GetInt32(),
                scheduleStatus = ScheduleLabel(Text(x, "scheduleStatus")), dueDate = Text(x, "dueDate"), plannedEnd = Text(x, "plannedEnd"),
                risk = x.GetProperty("risk").GetBoolean() ? "有" : "无"
            }).ToArray()
        };
    }

    private static object Metric(string label, int value, string unit) => new { label, value, unit };
    private static string Text(JsonElement value, string property) => value.GetProperty(property).GetString() ?? "";
    private static string ScheduleLabel(string value) => value switch { "full" => "已排完整", "partial" => "部分已排", _ => "尚未排入" };
    private static string WeekdayLabel(DayOfWeek value) => value switch
    {
        DayOfWeek.Monday => "星期一", DayOfWeek.Tuesday => "星期二", DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四", DayOfWeek.Friday => "星期五", DayOfWeek.Saturday => "星期六", _ => "星期日"
    };
    private static ToolResult Fail(string summary, string error) => new() { Success = false, Summary = summary, ErrorMessage = error };

    private static bool TryRange(string startText, string endText, string defaultStart, string defaultEnd, out DateOnly start, out DateOnly end, out string error)
    {
        startText = string.IsNullOrWhiteSpace(startText) ? defaultStart : startText.Trim();
        endText = string.IsNullOrWhiteSpace(endText) ? defaultEnd : endText.Trim();
        var valid = DateOnly.TryParseExact(startText, "yyyy-MM-dd", out start) && DateOnly.TryParseExact(endText, "yyyy-MM-dd", out end) && start <= end;
        error = valid ? "" : "日期使用 YYYY-MM-DD，且开始日期不能晚于结束日期。";
        if (valid && end.DayNumber - start.DayNumber > 62)
        {
            error = "单次甘特图最多查看 63 天，请缩小日期范围。";
            return false;
        }
        return valid;
    }

    private static JsonElement LoadSnapshot()
    {
        var assembly = typeof(ApsAnalysisTools).Assembly;
        using var stream = assembly.GetManifestResourceStream("WePilot.Agent.Data.production-demo.json")
            ?? throw new InvalidOperationException("Production sample missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
