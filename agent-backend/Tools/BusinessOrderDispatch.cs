using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

// Deterministic, read-only projection of the controlled order-dispatch snapshot.
public sealed class BusinessOrderDispatch
{
    private readonly ToolExecutionContext? _context;
    private readonly HashSet<string> _emitted = new();
    private static readonly JsonElement Snapshot = LoadSnapshot();

    public BusinessOrderDispatch(ToolExecutionContext context) => _context = context;

    public bool IsConfigured => true;

    [KernelFunction("get_order_dispatch_status")]
    [Description("只读核对内置受控演示数据中的订单是否已经形成派工在制任务、当前任务状态和未取得来源的任务。派工在制只表示运行中且仍有未完成数量，不表示已派给某人或已下发现场。")]
    public string GetOrderDispatchStatus(
        [Description("可选产线编码；空值表示全部产线")] string lineId = "",
        [Description("可选订单号；有订单号时只查看该订单及其关联当前任务")] string orderId = "")
        => Execute(lineId, orderId).ToModelJson();

    public ToolResult Execute(string lineId = "", string orderId = "")
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ToolResult result;
        try
        {
            result = Build(Snapshot, lineId, orderId);
        }
        catch (JsonException ex)
        {
            result = Fail("订单—派工演示数据不可用。", ex.Message);
        }

        _context?.ToolCalls.Add(new()
        {
            ToolName = "get_order_dispatch_status",
            Arguments = new { lineId, orderId },
            Success = result.Success,
            Summary = result.Summary,
            ElapsedMs = (int)watch.ElapsedMilliseconds
        });
        var key = $"{(lineId ?? "").Trim()}|{(orderId ?? "").Trim().ToUpperInvariant()}";
        if (result.Success && _context is not null && _emitted.Add(key)) _context.UiPayload.AddRange(result.UiPayload);
        return result;
    }

    public static ToolResult Build(JsonElement snapshot, string lineId = "", string orderId = "")
    {
        lineId = (lineId ?? "").Trim();
        orderId = (orderId ?? "").Trim().ToUpperInvariant();
        bool LineMatches(JsonElement row) => lineId.Length == 0 || Text(row, "lineId") == lineId;

        var orders = Elements(snapshot, "orders")
            .Where(LineMatches)
            .Where(row => orderId.Length == 0 || Text(row, "orderNo").Equals(orderId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (orderId.Length > 0 && orders.Length == 0)
            return Fail($"当前快照未找到演示订单 {orderId}。", "请核对订单号；不能据此判断该订单从未派工。");
        var selectedPlanIds = orders.Select(row => Text(row, "pdPlanGuid")).ToHashSet();
        var plans = Elements(snapshot, "plans").ToArray();
        var currentTasks = Elements(snapshot, "dispatchTasks").Where(LineMatches)
            .Where(row => orderId.Length == 0 || selectedPlanIds.Contains(Text(row, "pdPlanGuid"))).ToArray();
        var latestTasks = Elements(snapshot, "latestTasks").Where(LineMatches)
            .Where(row => orderId.Length == 0 || selectedPlanIds.Contains(Text(row, "pdPlanGuid"))).ToArray();
        var plansById = plans
            .Select(row => (Id: Text(row, "planGuid"), Row: row))
            .Where(item => item.Id.Length > 0)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First().Row);
        var ordersByPlan = orders
            .Select(row => (PlanId: Text(row, "pdPlanGuid"), Row: row))
            .Where(item => item.PlanId.Length > 0)
            .GroupBy(item => item.PlanId)
            .ToDictionary(group => group.Key, group => group.First().Row);
        var currentTaskIds = currentTasks.Select(row => Text(row, "taskGuid")).Where(value => value.Length > 0).ToHashSet();

        var orderRows = orders.Select(order =>
        {
            var planId = Text(order, "pdPlanGuid");
            var tasks = currentTasks.Where(task => Text(task, "pdPlanGuid") == planId).ToArray();
            var state = DeriveOrderState(tasks);
            var historyCount = latestTasks.Count(task =>
                Text(task, "pdPlanGuid") == planId && !currentTaskIds.Contains(Text(task, "taskGuid")));
            return OrderRow(order, plansById.TryGetValue(planId, out var plan) ? plan : default, tasks, state, historyCount);
        }).ToArray();

        var unmatchedTasks = currentTasks.Where(task => !ordersByPlan.ContainsKey(Text(task, "pdPlanGuid"))).ToArray();
        var linkedTasks = currentTasks.Where(task => ordersByPlan.ContainsKey(Text(task, "pdPlanGuid"))).ToArray();
        var taskRows = currentTasks.Select(task => TaskRow(
            task,
            ordersByPlan.TryGetValue(Text(task, "pdPlanGuid"), out var order) ? order : default,
            plansById.TryGetValue(Text(task, "pdPlanGuid"), out var plan) ? plan : default)).ToArray();

        var dispatchInProgressOrderCount = orderRows.Count(row => TextFromObject(row, "statusKey") == "dispatch_in_progress");
        var completedOrderCount = orderRows.Count(row => TextFromObject(row, "statusKey") == "dispatch_completed");
        var closedOrderCount = orderRows.Count(row => TextFromObject(row, "statusKey") == "dispatch_closed");
        var taskNotGeneratedOrderCount = orderRows.Count(row => TextFromObject(row, "statusKey") == "task_not_generated");
        var unknownOrderCount = orderRows.Count(row => TextFromObject(row, "statusKey") == "dispatch_status_unknown");
        var runTaskCount = currentTasks.Count(task => Status(task) == "run");
        var inProgressTaskCount = currentTasks.Count(IsInProgressTask);
        var finishTaskCount = currentTasks.Count(task => Status(task) == "finish");
        var closeTaskCount = currentTasks.Count(task => Status(task) == "close");
        var rangeLabel = lineId.Length == 0 ? "当前快照" : $"产线 {lineId} 当前快照";
        var summary = orderId.Length > 0
            ? currentTasks.Length == 0
                ? $"订单 {orderId} 在{rangeLabel}未找到当前任务；仅说明本次快照，原因尚未查明。"
                : $"订单 {orderId} 在{rangeLabel}的核对状态为{TextFromObject(orderRows[0], "status")}，关联 {currentTasks.Length} 条当前任务；不据此判断现场下发。"
            : $"{rangeLabel}包含 {orderRows.Length} 个订单，其中 {dispatchInProgressOrderCount} 个存在派工在制任务；{taskNotGeneratedOrderCount} 个订单尚未找到当前任务，另有 {unmatchedTasks.Length} 条当前任务暂未取得来源订单。";

        var riskRows = orderRows
            .Where(row => TextFromObject(row, "statusKey") is "task_not_generated" or "dispatch_status_unknown")
            .Select(row => new
            {
                orderNo = TextFromObject(row, "orderNo"),
                status = TextFromObject(row, "status"),
                reason = TextFromObject(row, "reason")
            }).ToArray();
        var riskHeadline = taskNotGeneratedOrderCount > 0
            ? $"{taskNotGeneratedOrderCount} 个订单在本次快照未找到当前任务；请结合排入计划状态核对，原因尚未查明。"
            : orderId.Length > 0 ? "该订单已取得当前任务状态。" : "当前订单均已取得当前任务状态。";

        var result = new ToolResult
        {
            Success = true,
            Summary = summary,
            AgentContext = new
            {
                source = "synthetic-demo",
                sourceLabel = Text(snapshot, "sourceLabel"),
                systemNo = Text(snapshot, "systemNo"),
                snapshotId = Text(snapshot, "snapshotId"),
                asOf = Text(snapshot, "asOf"),
                lineId = lineId.Length == 0 ? null : lineId,
                orderId = orderId.Length == 0 ? null : orderId,
                orderCount = orderRows.Length,
                dispatchInProgressOrderCount,
                completedOrderCount,
                closedOrderCount,
                taskNotGeneratedOrderCount,
                unknownOrderCount,
                currentTaskCount = currentTasks.Length,
                linkedTaskCount = linkedTasks.Length,
                unmatchedTaskCount = unmatchedTasks.Length,
                runTaskCount,
                inProgressTaskCount,
                finishTaskCount,
                closeTaskCount,
                latestTaskCount = latestTasks.Length,
                latestHistoryTaskCount = latestTasks.Count(task => !currentTaskIds.Contains(Text(task, "taskGuid"))),
                limitations = "本地受控合成演示数据，非实时查询；只确认订单与当前任务状态，不确认派工人、现场下发或上线状态。"
            },
            UiPayload = new()
            {
                new UiBlock
                {
                    Type = "order-dispatch",
                    Title = orderId.Length == 0 ? "订单—派工在制核对" : $"订单 {orderId} 派工状态核对",
                    Spec = new
                    {
                        viewType = "order-dispatch",
                        eyebrow = "APS READ-ONLY ORDER DISPATCH CHECK",
                        source = Text(snapshot, "source"),
                        sourceLabel = Text(snapshot, "sourceLabel"),
                        snapshotId = Text(snapshot, "snapshotId"),
                        asOf = Text(snapshot, "asOf"),
                        lineId = lineId.Length == 0 ? null : lineId,
                        orderId = orderId.Length == 0 ? null : orderId,
                        headline = summary,
                        releaseStatusNote = "当前快照未取得（不代表未下发）",
                        metrics = new object[]
                        {
                            Metric("订单总数", orderRows.Length, "个", "all-orders"),
                            Metric("派工在制", dispatchInProgressOrderCount, "个", "dispatch-in-progress"),
                            Metric("已完成", completedOrderCount, "个", "dispatch-completed"),
                            Metric("已关闭", closedOrderCount, "个", "dispatch-closed"),
                            Metric("未形成当前任务", taskNotGeneratedOrderCount, "个", "task-not-generated"),
                            Metric("来源未取得任务", unmatchedTasks.Length, "条", "unmatched-task")
                        },
                        orderFlow = new
                        {
                            available = true,
                            total = orderRows.Length,
                            dispatchInProgress = dispatchInProgressOrderCount,
                            completed = completedOrderCount,
                            closed = closedOrderCount,
                            taskNotGenerated = taskNotGeneratedOrderCount,
                            unknown = unknownOrderCount
                        },
                        status = new object[]
                        {
                            StatusItem("运行中", runTaskCount, "run", "blue"),
                            StatusItem("已完成", finishTaskCount, "finish", "green"),
                            StatusItem("已关闭", closeTaskCount, "close", "violet"),
                            StatusItem("来源未取得", unmatchedTasks.Length, "unmatched-task", "amber")
                        },
                        preview = orderRows.Take(3).Select(row => new
                        {
                            kind = "order",
                            primary = TextFromObject(row, "orderNo"),
                            secondary = $"{TextFromObject(row, "product")} · {TextFromObject(row, "line")}",
                            status = TextFromObject(row, "status")
                        }).ToArray(),
                        orderRows,
                        taskRows,
                        risk = new { headline = riskHeadline, items = riskRows },
                        detailGroups = new
                        {
                            allOrders = orderRows,
                            dispatchInProgress = orderRows.Where(row => TextFromObject(row, "statusKey") == "dispatch_in_progress").ToArray(),
                            dispatchCompleted = orderRows.Where(row => TextFromObject(row, "statusKey") == "dispatch_completed").ToArray(),
                            dispatchClosed = orderRows.Where(row => TextFromObject(row, "statusKey") == "dispatch_closed").ToArray(),
                            taskNotGenerated = orderRows.Where(row => TextFromObject(row, "statusKey") == "task_not_generated").ToArray(),
                            unknown = orderRows.Where(row => TextFromObject(row, "statusKey") == "dispatch_status_unknown").ToArray(),
                            allTasks = taskRows,
                            runTasks = taskRows.Where(row => TextFromObject(row, "statusKey") == "run").ToArray(),
                            finishTasks = taskRows.Where(row => TextFromObject(row, "statusKey") == "finish").ToArray(),
                            closeTasks = taskRows.Where(row => TextFromObject(row, "statusKey") == "close").ToArray(),
                            unmatchedTask = taskRows.Where(row => TextFromObject(row, "statusKey") == "unmatched-task").ToArray()
                        },
                        dataCheck = new
                        {
                            unmatchedTaskCount = unmatchedTasks.Length,
                            message = unmatchedTasks.Length > 0
                                ? $"{unmatchedTasks.Length} 条运行中任务在本次快照中暂未取得来源订单。这不代表任务异常，原因尚未查明。"
                                : "当前快照没有暂未取得来源订单的任务。"
                        },
                        limitations = new[]
                        {
                            "本场景为受控synthetic-demo，不代表实时业务库；订单组合、任务数量和状态分布用于展示业务关系。",
                            "派工在制依据关联当前任务状态为run且taskQuantity-deliveryQuantity大于0；订单核对状态和统计指标由固定规则派生。",
                            "当前快照没有现场下发或流转凭证，因此页面不对现场下发状态作判断。",
                            "任务来源未取得只描述当前快照事实，不计入订单总数；原因尚未查明。",
                            "本场景只读，不执行派单、下发、重排或数据库写入。"
                        }
                    }
                }
            }
        };
        return result;
    }

    private static object OrderRow(JsonElement order, JsonElement plan, JsonElement[] tasks, OrderState state, int historyCount)
    {
        var planRow = plan.ValueKind == JsonValueKind.Undefined ? order : plan;
        var reason = state.Key switch
        {
            "dispatch_in_progress" => "存在运行中且仍有未完成数量的当前任务；仅确认派工在制，不确认现场下发。",
            "dispatch_completed" => "关联当前任务均已完成；当前不存在派工在制任务。",
            "dispatch_closed" => "关联当前任务均已关闭；当前不存在派工在制任务。",
            "task_not_generated" when historyCount > 0 => $"本次快照未找到当前任务；最近版本存在 {historyCount} 条历史任务记录，原因尚未查明。",
            "task_not_generated" when plan.ValueKind == JsonValueKind.Undefined => "当前未排入计划，也未找到当前任务；原因尚未查明。",
            "task_not_generated" => "已排入计划，但本次快照未找到关联任务；原因尚未查明。",
            _ => "任务状态组合无法按当前规则归类，暂无法确认。"
        };
        return new
        {
            rowType = "order",
            id = Text(order, "orderNo"),
            number = Text(order, "orderNo"),
            orderNo = Text(order, "orderNo"),
            product = Text(order, "productName"),
            productNo = Text(order, "productNo"),
            line = Text(order, "lineId"),
            start = Text(planRow, "planStart"),
            end = Text(planRow, "planEnd"),
            quantity = Number(order, "quantity") ?? Number(planRow, "planQuantity"),
            dispatchTaskCount = tasks.Length,
            inProgressTaskCount = tasks.Count(IsInProgressTask),
            taskStatus = tasks.Length == 0 ? "未形成当前任务" : string.Join("、", tasks.Select(task => StatusLabel(Status(task))).Distinct()),
            status = state.Label,
            statusKey = state.Key,
            taskTag = state.Key == "dispatch_in_progress" ? "T" : "—",
            releaseStatus = "下发/流转状态未取得",
            reason,
            risk = state.Key is "task_not_generated" or "dispatch_status_unknown" ? "attention" : "normal",
            latestHistoryTaskCount = historyCount,
            relation = "订单"
        };
    }

    private static object TaskRow(JsonElement task, JsonElement order, JsonElement plan)
    {
        var matched = order.ValueKind != JsonValueKind.Undefined;
        var status = Status(task);
        var planRow = plan.ValueKind == JsonValueKind.Undefined ? task : plan;
        return new
        {
            rowType = "task",
            id = Text(task, "taskGuid"),
            number = Text(task, "taskNo"),
            orderNo = matched ? Text(order, "orderNo") : "",
            product = Text(task, "productName"),
            productNo = Text(task, "productNo"),
            line = Text(task, "lineId"),
            start = Text(task, "taskStart"),
            end = Text(task, "taskEnd"),
            quantity = Number(task, "taskQuantity"),
            taskQuantity = Number(task, "taskQuantity"),
            deliveryQuantity = Number(task, "deliveryQuantity"),
            remainingQuantity = Math.Max(0, (Number(task, "taskQuantity") ?? 0) - (Number(task, "deliveryQuantity") ?? 0)),
            dispatchTaskCount = 1,
            inProgressTaskCount = IsInProgressTask(task) ? 1 : 0,
            taskStatus = StatusLabel(status),
            status = StatusLabel(status),
            statusKey = matched ? TaskFilter(status) : "unmatched-task",
            taskTag = IsInProgressTask(task) ? "T" : "—",
            releaseStatus = "下发/流转状态未取得",
            reason = matched
                ? $"关联订单 {Text(order, "orderNo")}；仅确认当前任务状态，不确认现场下发。"
                : "运行中任务暂未取得来源订单，原因尚未查明。",
            risk = matched ? "normal" : "attention",
            planNo = matched ? Text(planRow, "planNo") : "",
            relation = matched ? "订单当前任务" : "任务来源未取得"
        };
    }

    private static OrderState DeriveOrderState(JsonElement[] tasks)
    {
        if (tasks.Any(IsInProgressTask)) return new("dispatch_in_progress", "派工在制");
        if (tasks.Length > 0 && tasks.All(task => Status(task) == "finish")) return new("dispatch_completed", "已完成");
        if (tasks.Length > 0 && tasks.All(task => Status(task) == "close")) return new("dispatch_closed", "已关闭");
        if (tasks.Length == 0) return new("task_not_generated", "未形成当前任务");
        return new("dispatch_status_unknown", "状态待确认");
    }

    private static bool IsInProgressTask(JsonElement row)
        => Status(row) == "run" && (Number(row, "taskQuantity") ?? 0) - (Number(row, "deliveryQuantity") ?? 0) > 0;

    private static string TaskFilter(string status) => status switch
    {
        "run" => "run",
        "finish" => "finish",
        "close" => "close",
        _ => "unknown"
    };

    private static string Status(JsonElement row) => Text(row, "status").Trim().ToLowerInvariant();

    private static string StatusLabel(string value) => value switch
    {
        "run" => "运行中",
        "finish" => "已完成",
        "close" => "已关闭",
        _ => "暂无法确认"
    };

    private static object Metric(string label, int value, string unit, string filter) => new
    {
        label,
        value = value.ToString(CultureInfo.InvariantCulture),
        unit,
        filter,
        available = true
    };

    private static object StatusItem(string label, int value, string filter, string tone) => new { label, value, filter, tone };

    private static JsonElement[] Elements(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

    private static string Text(JsonElement row, string name)
        => row.ValueKind != JsonValueKind.Undefined && row.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : "";

    private static string TextFromObject(object row, string name)
        => JsonSerializer.SerializeToElement(row).TryGetProperty(name, out var value) ? value.ToString() : "";

    private static double? Number(JsonElement row, string name)
        => row.ValueKind != JsonValueKind.Undefined && row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private static ToolResult Fail(string summary, string error) => new() { Success = false, Summary = summary, ErrorMessage = error };

    private static JsonElement LoadSnapshot()
    {
        var assembly = typeof(BusinessOrderDispatch).Assembly;
        using var stream = assembly.GetManifestResourceStream("WePilot.Agent.Data.production-demo.json")
            ?? throw new InvalidOperationException("Production demo snapshot missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("orderDispatch").Clone();
    }

    private sealed record OrderState(string Key, string Label);
}
