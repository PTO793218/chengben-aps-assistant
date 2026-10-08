using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

public sealed class ProductionTools
{
    private readonly ToolExecutionContext _context;
    private readonly HashSet<string> _emittedFilters = new();
    public ProductionTools(ToolExecutionContext context) => _context = context;
    private static readonly JsonElement Snapshot = LoadSnapshot();
    private static JsonElement LoadSnapshot()
    {
        var assembly = typeof(ProductionTools).Assembly;
        using var stream = assembly.GetManifestResourceStream("WePilot.Agent.Data.production-demo.json")
            ?? throw new InvalidOperationException("Production sample missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    [KernelFunction("get_production_plan")]
    [Description("封存兼容工具：查询本地合成生产计划示例并返回旧三维视图。仅供历史会话兼容，不作为当前模型业务工具。")]
    public string GetProductionPlan(
        [Description("订单号，例如 Z9900001；不筛选时为空")] string orderId = "",
        [Description("生产线ID或完整名称；不筛选时为空")] string lineId = "",
        [Description("all=全部未完工，risk=计划交期风险，pending=含待排任务的订单")] string scope = "all",
        [Description("可选具体日期 YYYY-MM-DD；不筛选时为空")] string date = "")
        => Execute(orderId, lineId, scope, date).ToModelJson();

    public ToolResult Execute(string orderId = "", string lineId = "", string scope = "all", string date = "")
    {
        orderId = (orderId ?? "").Trim(); lineId = (lineId ?? "").Trim();
        scope = (scope ?? "all").ToLowerInvariant(); date = (date ?? "").Trim();
        if (scope is not ("all" or "risk" or "pending") ||
            (date.Length > 0 && !DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)))
            return new() { Success = false, Summary = "筛选参数无效。", ErrorMessage = "scope 使用 all/risk/pending，日期使用 YYYY-MM-DD。" };
        var lines = Snapshot.GetProperty("lines").EnumerateArray().ToArray();
        if (lineId.Length > 0)
        {
            var line = lines.FirstOrDefault(l => l.GetProperty("id").GetString() == lineId || l.GetProperty("name").GetString() == lineId);
            if (line.ValueKind == JsonValueKind.Undefined)
                return new() { Success = false, Summary = "示例中未找到该产线。", ErrorMessage = "请从已有产线中选择。" };
            lineId = line.GetProperty("id").GetString()!;
        }
        var tasks = Snapshot.GetProperty("tasks").EnumerateArray().ToArray();
        var pending = Snapshot.GetProperty("pending").EnumerateArray().ToArray();
        var orders = Snapshot.GetProperty("orders").EnumerateArray().Where(o =>
            o.GetProperty("productionStatus").GetString() != "completed" &&
            (orderId.Length == 0 || o.GetProperty("id").GetString() == orderId) &&
            (scope != "risk" || o.GetProperty("risk").GetBoolean()) &&
            (scope != "pending" || pending.Any(t => t.GetProperty("orderId").GetString() == o.GetProperty("id").GetString())) &&
            (lineId.Length == 0 && date.Length == 0 || tasks.Any(t =>
                t.GetProperty("orderId").GetString() == o.GetProperty("id").GetString() &&
                (lineId.Length == 0 || t.GetProperty("lineId").GetString() == lineId) &&
                (date.Length == 0 || t.GetProperty("date").GetString() == date)))).ToArray();
        var ids = orders.Select(o => o.GetProperty("id").GetString()).ToHashSet();
        var visibleTasks = tasks.Where(t => ids.Contains(t.GetProperty("orderId").GetString()) &&
            (lineId.Length == 0 || t.GetProperty("lineId").GetString() == lineId) &&
            (date.Length == 0 || t.GetProperty("date").GetString() == date)).ToArray();
        var visiblePending = pending.Where(t => ids.Contains(t.GetProperty("orderId").GetString())).ToArray();
        var full = orders.Count(o => o.GetProperty("scheduleStatus").GetString() == "full");
        var partial = orders.Count(o => o.GetProperty("scheduleStatus").GetString() == "partial");
        var risk = orders.Count(o => o.GetProperty("risk").GetBoolean());
        var summary = $"本地合成示例，非实时业务。当前筛选有 {orders.Length} 个未完工订单：{full} 个已排完整、{partial} 个部分已排、{orders.Length-full-partial} 个尚未排入；{risk} 个有计划交期风险。样本另含 {visiblePending.Length} 项未排占位记录，其业务名称和口径未确认。已排完整不等于生产完成。";
        var result = new ToolResult
        {
            Success = true, Summary = summary,
            AgentContext = new { source = "synthetic-demo", snapshotId = Snapshot.GetProperty("id").GetString(),
                asOf = Snapshot.GetProperty("asOf").GetString(), orderId, lineId, scope, date,
                orderCount = orders.Length, fullyScheduled = full, partiallyScheduled = partial, riskCount = risk,
                pendingCount = visiblePending.Length, orders, lines, scheduledTaskCount = visibleTasks.Length,
                detailedTasksIncluded = orderId.Length > 0 || lineId.Length > 0 || date.Length > 0,
                tasks = orderId.Length > 0 || lineId.Length > 0 || date.Length > 0 ? visibleTasks : Array.Empty<JsonElement>(), pending = visiblePending,
                limitations = "任务数量不能跨工段累加为成品产量。没有真实产能、缺料原因或执行实时数据，不推算负荷率，不承诺交付，不执行重排。" },
            UiPayload = new() { new() { Type = "production-plan", Title = "生产计划概况",
                Spec = new { snapshot = Snapshot, focus = new { orderId, lineId, scope, date } } } }
        };
        _context.ToolCalls.Add(new() { ToolName = "get_production_plan", Success = true, Summary = summary });
        if (_emittedFilters.Add(JsonSerializer.Serialize(new { orderId, lineId, scope, date })))
            _context.UiPayload.AddRange(result.UiPayload);
        return result;
    }
}
