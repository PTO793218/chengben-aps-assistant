using System.Globalization;
using System.Text.Json;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

// Fixed, read-only projection of an explicitly exported business version. No SQL or code supplied by the LLM.
public sealed class BusinessOrderSchedule
{
    private readonly IConfiguration _config;
    public BusinessOrderSchedule(IConfiguration config) => _config = config;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["BusinessData:OrderSnapshotPath"]);

    public ToolResult Execute(string orderId, string version, string startDate, string endDate)
    {
        if (!IsConfigured) return Fail("真实订单快照尚未配置。", "无法查询，不回退到合成数据。");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_config["BusinessData:OrderSnapshotPath"]!));
            return Build(doc.RootElement, orderId, version, startDate, endDate);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Fail("真实订单快照不可用。", "请检查或刷新快照；没有使用合成数据替代。");
        }
    }

    public static ToolResult Build(JsonElement snapshot, string orderId, string version = "", string startDate = "", string endDate = "")
    {
        orderId = orderId.Trim().ToUpperInvariant();
        if (orderId.Length == 0) return Fail("需要完整订单号。", "请提供订单号。");
        var actualVersion = Text(snapshot, "version");
        if (version.Length > 0 && version != actualVersion)
            return Fail($"当前真实快照仅包含版本 {actualVersion}。", "指定版本不在快照中，不自动替换版本。");
        var orders = snapshot.GetProperty("orders").EnumerateArray().Where(x => Text(x, "orderId") == orderId).ToArray();
        if (orders.Length == 0) return Fail($"真实快照版本 {actualVersion} 中未找到订单 {orderId}。", "仅查询该版本 order_sn>0 的需求，不代表业务系统中不存在此订单。");
        var ids = orders.Select(x => Text(x, "calcGuid")).ToHashSet();
        var all = snapshot.GetProperty("tasks").EnumerateArray().Where(x => Text(x, "orderId") == orderId && ids.Contains(Text(x, "calcGuid"))).ToArray();
        var defaultStart = all.Length == 0 ? Text(snapshot, "asOf")[..10] : all.Min(x => Text(x, "date"))!;
        var defaultEnd = all.Length == 0 ? defaultStart : all.Max(x => Text(x, "date"))!;
        startDate = string.IsNullOrWhiteSpace(startDate) ? defaultStart : startDate.Trim();
        endDate = string.IsNullOrWhiteSpace(endDate) ? defaultEnd : endDate.Trim();
        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", out var start) || !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", out var end) || end < start || end.DayNumber - start.DayNumber > 62)
            return Fail("日期范围无效。", "使用 YYYY-MM-DD，开始不晚于结束，单次最多63天；未指定时取该订单全部计划日期。");
        var tasks = all.Where(x => string.CompareOrdinal(Text(x, "date"), startDate) >= 0 && string.CompareOrdinal(Text(x, "date"), endDate) <= 0).ToArray();
        if (tasks.Any(x => x.GetProperty("mappingCount").GetInt32() != 1 || Text(x, "shift") is not ("day" or "night") || Text(x, "date") != Text(x, "endDate")))
            return Fail("该订单包含暂不能准确展示的计划记录。", "工段映射、班次或跨日记录需要核对；未猜测或省略记录。");
        var groups = tasks.GroupBy(Key).ToArray();
        var rowIndex = groups.Select((g, i) => (g.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var rows = groups.Select(g =>
        {
            var first = g.First();
            return new { key = g.Key, lineName = Text(first, "lineName"), productName = Text(first, "productName"),
                productSpec = Text(first, "productNo"), stage = Text(first, "stage"), operation = Text(first, "operation"),
                bomLevel = Text(first, "bomLevel"), calcGuid = Text(first, "calcGuid"),
                planQuantity = g.All(IsPlaceholder) ? (double?)null : g.Sum(x => Number(x, "quantity")), orderId,
                planStart = g.Min(x => Text(x, "date")), planEnd = g.Max(x => Text(x, "endDate")) };
        }).ToArray();
        var columns = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).SelectMany(offset =>
        {
            var date = start.AddDays(offset);
            var weekday = new[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" }[(int)date.DayOfWeek];
            return new[] { "白班", "晚班" }.Select((shift, i) => new { key = $"{date:yyyy-MM-dd}-{i}", date = date.ToString("yyyy-MM-dd"), weekday, shift });
        }).ToArray();
        var items = tasks.Select(x => new
        {
            id = Text(x, "id"), rowIndex = rowIndex[Key(x)],
            columnIndex = (DateOnly.Parse(Text(x, "date"), CultureInfo.InvariantCulture).DayNumber - start.DayNumber) * 2 + (Text(x, "shift") == "night" ? 1 : 0),
            orderId, stage = Text(x, "stage"), operation = Text(x, "operation"), date = Text(x, "date"),
            shift = Shift(x), lineId = Text(x, "lineId"), quantity = Number(x, "quantity"),
            label = IsPlaceholder(x) ? "周期" : Number(x, "quantity").ToString("0.########", CultureInfo.InvariantCulture),
            plannedHours = Number(x, "plannedHours"), changeHours = Number(x, "changeHours"), tone = Text(x, "shift") == "night" ? "night" : "day"
        }).ToArray();
        var stageCount = tasks.Select(x => Text(x, "segmentNo")).Distinct().Count();
        var lineCount = tasks.Select(x => Text(x, "lineId")).Distinct().Count();
        var cycleCount = tasks.Count(IsPlaceholder);
        var summary = $"真实快照版本 {actualVersion}：订单 {orderId} 在 {startDate} 至 {endDate} 有 {tasks.Length} 条计划记录，涉及 {stageCount} 个工段、{lineCount} 条产线。" +
            (tasks.Length == 0 ? "该范围无计划记录，不据此判断订单未排或完工。" : "这是已有计划，不代表实际完工或物料齐套。");
        var lineLoad = BusinessLineLoad.Build(snapshot, orderId, all);
        var facts = new { source = "business-snapshot", snapshotId = actualVersion, asOf = Text(snapshot, "asOf"), orderId,
            viewType = "order", version = actualVersion, range = new { start = startDate, end = endDate },
            lineLoadSummary = lineLoad.Summary, demandCount = orders.Length, scheduledTaskCount = tasks.Length, stageCount, lineCount, cycleCount,
            planStart = tasks.Length == 0 ? null : tasks.Min(x => Text(x, "date")), planEnd = tasks.Length == 0 ? null : tasks.Max(x => Text(x, "endDate")),
            limitations = "真实只读快照，非实时查询。未判断排入完整性、实际完工、齐套或交付风险；不可跨工段累加成品量。" };
        return new ToolResult
        {
            Success = true, Summary = summary, AgentContext = facts,
            UiPayload = new() { new() { Type = "aps-analysis", Title = $"订单 {orderId} 排程", Spec = new
            {
                viewType = "order", eyebrow = "APS READ-ONLY QUERY", source = "business-snapshot", sourceLabel = "业务库真实快照 · 非实时查询",
                snapshotId = actualVersion, asOf = Text(snapshot, "asOf"), range = new { start = startDate, end = endDate }, headline = summary,
                metrics = new[] { Metric("计划记录", tasks.Length, "条"), Metric("涉及工段", stageCount, "个"), Metric("涉及产线", lineCount, "条"), Metric("计划日期", tasks.Select(x => Text(x, "date")).Distinct().Count(), "天") },
                lineLoad = lineLoad,
                chart = new { kind = "gantt", title = "真实订单排程甘特", rows, columns, items, emptyText = "该范围没有计划记录。",
                    fixedColumns = new[] { Col("lineName", "生产线"), Col("productName", "物料名称"), Col("productSpec", "物料编码"), Col("stage", "工段"), Col("operation", "代表工序"), Col("planQuantity", "范围内计划量"), Col("orderId", "订单号"), Col("planStart", "计划开始"), Col("planEnd", "计划结束") } },
                detail = new { title = "原始计划明细（逐条可核对）", columns = new[] { Col("date", "日期"), Col("shift", "班次"), Col("stage", "工段"), Col("lineId", "产线"), Col("quantity", "计划量"), Col("plannedHours", "加工工时"), Col("changeHours", "换型工时"), Col("productNo", "物料"), Col("operation", "工序"), Col("kind", "记录类型"), Col("bomLevel", "BOM层级"), Col("id", "计划标识"), Col("calcGuid", "需求标识") },
                    rows = tasks.Select(x => new { id = Text(x, "id"), calcGuid = Text(x, "calcGuid"), productNo = Text(x, "productNo"), bomLevel = Text(x, "bomLevel"), stage = Text(x, "stage"), operation = Text(x, "operation"), lineId = Text(x, "lineId"), date = Text(x, "date"), shift = Shift(x), quantity = Number(x, "quantity"), plannedHours = Number(x, "plannedHours"), changeHours = Number(x, "changeHours"), kind = IsPlaceholder(x) ? "周期/外协占位" : "数量计划" }).ToArray() },
                limitations = new[] { $"来源：现有业务系统的需求与排程记录，版本 {actualVersion}；快照后发生的变化尚未反映。", "按计划开始日期筛选，仅展示已有安排；同格多笔计划分别保留。", "周期/外协零数量记录显示‘周期’，原始数量保留在明细中；计划数不能跨工段相加为成品数量。", "本页不判断实际进度、物料齐套、整单是否排完或能否按期交付。" }
            } } }
        };
    }
    private static string Key(JsonElement x) => string.Join("|", new[] { "calcGuid", "bomLevel", "productNo", "segmentNo", "operationNo", "craftSequence", "lineId", "schedulingTag", "assistTag" }.Select(k => x.GetProperty(k).ToString()));
    private static bool IsPlaceholder(JsonElement x) => (Text(x, "schedulingTag") == "T" || Text(x, "assistTag") == "T") && Number(x, "quantity") == 0;
    private static string Shift(JsonElement x) => Text(x, "shift") == "night" ? "晚班" : "白班";
    private static string Text(JsonElement x, string key) => x.GetProperty(key).GetString() ?? "";
    private static double Number(JsonElement x, string key) => x.GetProperty(key).ValueKind == JsonValueKind.Null ? 0 : x.GetProperty(key).GetDouble();
    private static object Col(string key, string label) => new { key, label };
    private static object Metric(string label, int value, string unit) => new { label, value, unit };
    private static ToolResult Fail(string summary, string error) => new() { Summary = summary, ErrorMessage = error };
}
