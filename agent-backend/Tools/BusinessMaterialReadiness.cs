using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

// Deterministic, read-only projection of the embedded presentation snapshot.
// The snapshot already contains the demo fullTag result; this class presents it
// and its linked evidence without recalculating readiness in the application.
public sealed class BusinessMaterialReadiness
{
    private readonly ToolExecutionContext? _context;
    private readonly HashSet<string> _emitted = new();
    private static readonly JsonElement Snapshot = LoadSnapshot();

    public BusinessMaterialReadiness(ToolExecutionContext context) => _context = context;

    public bool IsConfigured => true;

    [KernelFunction("get_material_readiness")]
    [Description("只读查询内置业务场景演示数据中的订单—物料齐套标记、BOM物料和库存/供应证据。all=查看范围摘要；order=查看指定订单；material=按物料名称或物料号追查影响订单。不得在应用层重新计算齐套。")]
    public string GetMaterialReadiness(
        [Description("all=范围摘要；order=指定订单；material=按物料名称或物料号查影响订单")] string scope = "all",
        [Description("scope=order时填写订单号，例如Z9900001；不要要求用户记忆内部物料号")] string orderId = "",
        [Description("scope=material时填写用户说的物料名称或物料号，例如SKF4-56、钢材；支持快照内包含的文本")] string materialQuery = "",
        [Description("可选APS需求版本；留空使用快照当前版本")] string version = "")
        => Execute(scope, orderId, materialQuery, version).ToModelJson();

    public ToolResult Execute(string scope = "all", string orderId = "", string materialQuery = "", string version = "")
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ToolResult result;
        try
        {
            result = Build(Snapshot, scope, orderId, materialQuery, version);
        }
        catch (JsonException ex)
        {
            result = Fail("物料齐套演示数据不可用。", ex.Message);
        }

        _context?.ToolCalls.Add(new()
        {
            ToolName = "get_material_readiness",
            Arguments = new { scope, orderId, materialQuery, version },
            Success = result.Success,
            Summary = result.Summary,
            ElapsedMs = (int)watch.ElapsedMilliseconds
        });
        var key = $"{scope}|{orderId}|{materialQuery}|{version}";
        if (result.Success && _context is not null && _emitted.Add(key)) _context.UiPayload.AddRange(result.UiPayload);
        return result;
    }

    public static ToolResult Build(JsonElement snapshot, string scope = "all", string orderId = "", string materialQuery = "", string version = "")
    {
        scope = NormalizeScope(scope);
        orderId = (orderId ?? "").Trim();
        materialQuery = (materialQuery ?? "").Trim();
        version = (version ?? "").Trim();

        if (scope is not ("all" or "order" or "material"))
            return Fail("不支持的查询范围。", "scope 使用 all、order 或 material。");
        if (scope == "order" && orderId.Length == 0)
            return Fail("缺少订单号。", "查看指定订单的物料齐套详情时必须提供订单号。");
        if (scope == "material" && materialQuery.Length == 0)
            return Fail("缺少物料关键词。", "按物料查询时请提供物料名称或物料号。");

        var snapshotVersion = Text(snapshot, "demandVer");
        if (version.Length > 0 && snapshotVersion.Length > 0 && !string.Equals(version, snapshotVersion, StringComparison.OrdinalIgnoreCase))
            return Fail("快照版本不一致。", $"请求版本 {version} 与当前只读快照 {snapshotVersion} 不一致，请刷新后重试。");

        var allOrders = Elements(snapshot, "orders");
        var allStatuses = Elements(snapshot, "orderStatuses");
        var allBomRows = Elements(snapshot, "productBomMaterials");
        var allReadinessRows = Elements(snapshot, "readinessResults");
        var allImpactRows = Elements(snapshot, "materialImpactSummary");

        var selectedOrders = SelectOrders(scope, orderId, materialQuery, allOrders, allBomRows, allReadinessRows, allImpactRows);
        if (scope == "order" && selectedOrders.Length == 0)
            return Fail("未找到该订单。", $"当前快照中没有订单 {orderId}，未使用其他订单替代。");
        if (scope == "material" && selectedOrders.Length == 0)
            return Fail("当前快照没有匹配的物料记录。", $"未找到与“{materialQuery}”匹配的物料、BOM或齐套结果。");
        if (scope == "all" && allOrders.Length == 0)
            return Fail("当前快照没有订单记录。", "无法形成订单—物料矩阵，不使用合成数据替代。");

        var statusByOrder = allStatuses
            .Where(x => Text(x, "orderId").Length > 0)
            .GroupBy(x => Text(x, "orderId"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var readinessByKey = allReadinessRows
            .Where(x => Text(x, "orderId").Length > 0 && Text(x, "materialNo").Length > 0)
            .GroupBy(x => $"{Text(x, "orderId")}|{Text(x, "materialNo")}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var summary = BuildSummary(selectedOrders, statusByOrder, allReadinessRows);
        var displayOrders = scope == "all" ? RepresentativeOrders(selectedOrders, statusByOrder, 18) : selectedOrders.Take(24).ToArray();
        var matchedMaterialNos = scope == "material"
            ? MatchingMaterialNos(materialQuery, allBomRows, allReadinessRows, allImpactRows)
            : Array.Empty<string>();
        var columns = BuildColumns(displayOrders, scope, materialQuery, matchedMaterialNos, allBomRows, allReadinessRows);
        var rows = displayOrders.Select(order => BuildOrderRow(order, statusByOrder, allBomRows, allReadinessRows, readinessByKey, columns, scope, materialQuery)).ToArray();
        var impact = BuildImpact(allImpactRows, scope == "material" ? materialQuery : "", matchedMaterialNos);
        var selectedOrderIds = selectedOrders.Select(order => Text(order, "orderId")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var readinessResultRowCount = allReadinessRows.Count(row => selectedOrderIds.Contains(Text(row, "orderId")));
        var summaryText = BuildHeadline(scope, orderId, materialQuery, summary, readinessResultRowCount);
        var limitations = BuildLimitations(snapshot);

        var result = new ToolResult
        {
            Success = true,
            Summary = summaryText,
            AgentContext = new
            {
                source = "synthetic-demo",
                sourceLabel = "本地业务场景演示数据 · 非实时",
                systemNo = Text(snapshot, "systemNo"),
                snapshotId = snapshotVersion,
                asOf = Text(snapshot, "asOf"),
                scope,
                orderId = orderId.Length == 0 ? null : orderId,
                materialQuery = materialQuery.Length == 0 ? null : materialQuery,
                totalOrders = summary.TotalOrders,
                readyOrders = summary.ReadyOrders,
                notObtainedOrders = summary.NotObtainedOrders,
                unknownOrders = summary.UnknownOrders,
                coveragePercent = summary.CoveragePercent,
                displayedOrderCount = rows.Length,
                readinessResultRowCount,
                limitations = "本地合成演示数据，非实时查询；totalOrders 是订单数，readinessResultRowCount 是物料结果条数，不可混用；页面沿用场景预置的fullTag标记，不在应用层重算齐套，不把未获得标记直接解释为已确认短缺。"
            },
            UiPayload = new()
            {
                new UiBlock
                {
                    Type = "material-readiness",
                    Title = "订单—物料齐套分析",
                    Spec = new
                    {
                        viewType = "material-readiness",
                        eyebrow = "APS MATERIAL READINESS",
                        source = "synthetic-demo",
                        sourceLabel = "本地业务场景演示数据 · 非实时",
                        snapshotId = $"{Text(snapshot, "snapshotId")} · 系统{Text(snapshot, "systemNo")}",
                        asOf = Text(snapshot, "asOf"),
                        headline = summaryText,
                        metrics = new object[]
                        {
                            Metric("订单总数", summary.TotalOrders, "单", "blue"),
                            Metric("已有齐套标记", summary.ReadyOrders, "单", "ready"),
                            Metric("未获得齐套标记", summary.NotObtainedOrders, "单", "not-obtained"),
                            Metric("状态未取得", summary.UnknownOrders, "单", "unknown")
                        },
                        model = new
                        {
                            snapshotId = snapshotVersion.Length == 0 ? "—" : snapshotVersion,
                            systemNo = Text(snapshot, "systemNo").Length == 0 ? "—" : Text(snapshot, "systemNo"),
                            asOf = Text(snapshot, "asOf"),
                            source = "synthetic-demo",
                            columns,
                            rows,
                            summary = new
                            {
                                totalOrders = summary.TotalOrders,
                                readyOrders = summary.ReadyOrders,
                                notObtainedOrders = summary.NotObtainedOrders,
                                unknownOrders = summary.UnknownOrders,
                                coveragePercent = summary.CoveragePercent
                            },
                            impact,
                            limitations
                        },
                        limitations
                    }
                }
            }
        };
        return result;
    }

    private static object BuildOrderRow(
        JsonElement order,
        IReadOnlyDictionary<string, JsonElement> statusByOrder,
        JsonElement[] bomRows,
        JsonElement[] readinessRows,
        IReadOnlyDictionary<string, JsonElement> readinessByKey,
        object[] columns,
        string scope,
        string materialQuery)
    {
        var id = Text(order, "orderId");
        statusByOrder.TryGetValue(id, out var savedStatus);
        var orderStatus = StatusObject(OrderStatusKey(savedStatus, readinessRows.Where(x => Text(x, "orderId") == id).ToArray()));
        var riskSource = savedStatus.ValueKind != JsonValueKind.Undefined ? savedStatus : order;
        var productNo = Text(order, "productNo");
        var productBom = bomRows.Where(x => ProductMatches(x, productNo) && IsDetailBom(x)).ToArray();
        var orderEvidence = readinessRows.Where(x => Text(x, "orderId") == id).ToArray();
        var materials = columns.Select(column =>
        {
            var materialNo = ObjectText(column, "materialNo");
            var bom = productBom.FirstOrDefault(x => Text(x, "materialNo") == materialNo);
            var hasBom = !string.IsNullOrWhiteSpace(Text(bom, "materialNo"));
            var evidence = readinessByKey.TryGetValue($"{id}|{materialNo}", out var savedEvidence) ? savedEvidence : (JsonElement?)null;
            var isApplicable = hasBom || evidence is not null;
            var cellStatus = evidence is not null
                ? StatusObject(MaterialStatusKey(evidence.Value))
                : StatusObject(isApplicable ? "unknown" : "not-applicable");
            return new
            {
                materialNo,
                materialName = ObjectText(column, "materialName"),
                label = ObjectText(column, "label"),
                applicable = isApplicable,
                status = cellStatus,
                evidence = evidence is null ? null : Evidence(evidence.Value)
            };
        }).ToArray();

        return new
        {
            orderId = id,
            productNo,
            productName = Text(order, "productName"),
            orderQuantity = Number(order, "orderQuantity"),
            dueDate = Text(order, "dueDate"),
            status = orderStatus,
            risk = Risk(riskSource),
            materials
        };
    }

    private static object[] BuildColumns(JsonElement[] orders, string scope, string materialQuery, string[] matchedMaterialNos, JsonElement[] bomRows, JsonElement[] readinessRows)
    {
        var candidates = new List<(string No, string Name)>();
        foreach (var order in orders)
        {
            var productNo = Text(order, "productNo");
            foreach (var row in bomRows.Where(x => ProductMatches(x, productNo) && IsDetailBom(x)))
                AddMaterial(candidates, Text(row, "materialNo"), Text(row, "materialName"));
        }
        foreach (var row in readinessRows.Where(x => orders.Any(order => Text(order, "orderId") == Text(x, "orderId"))))
            AddMaterial(candidates, Text(row, "materialNo"), Text(row, "materialName"));

        if (scope == "material")
        {
            var filtered = candidates.Where(x => MaterialMatches(x.No, x.Name, materialQuery) || matchedMaterialNos.Contains(x.No, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var materialNo in matchedMaterialNos)
            {
                if (filtered.Any(x => string.Equals(x.No, materialNo, StringComparison.OrdinalIgnoreCase))) continue;
                var source = bomRows.Concat(readinessRows).FirstOrDefault(x => string.Equals(Text(x, "materialNo"), materialNo, StringComparison.OrdinalIgnoreCase));
                filtered.Add((materialNo, source.ValueKind == JsonValueKind.Undefined ? materialNo : Text(source, "materialName")));
            }
            candidates = filtered;
        }

        return candidates
            .Where(x => x.No.Length > 0)
            .GroupBy(x => x.No, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(14)
            .Select(x => new { materialNo = x.No, materialName = x.Name.Length == 0 ? x.No : x.Name, label = ShortMaterialName(x.Name, x.No) })
            .Cast<object>()
            .ToArray();
    }

    private static object[] BuildImpact(JsonElement[] rows, string materialQuery, string[] matchedMaterialNos)
        => rows
            .Where(x => materialQuery.Length == 0 || matchedMaterialNos.Contains(Text(x, "materialNo"), StringComparer.OrdinalIgnoreCase) || Text(x, "materialNo").Contains(materialQuery, StringComparison.OrdinalIgnoreCase))
            .Take(50)
            .Select(x => new
            {
                materialNo = Text(x, "materialNo"),
                orderCount = Integer(x, "orderCount"),
                orderIds = StringArray(x, "orderIds"),
                bomRowCount = Integer(x, "bomRowCount")
            })
            .Cast<object>()
            .ToArray();

    private static object Evidence(JsonElement row) => new
    {
        materialNo = Text(row, "materialNo"),
        materialName = Text(row, "materialName"),
        materialNum = Number(row, "materialNum"),
        remainNum = Number(row, "remainNum"),
        thisRemainNum = Number(row, "thisRemainNum"),
        totalRemainNum = Number(row, "totalRemainNum"),
        supplyOrderNo = Text(row, "supplyOrderNo"),
        supplyDeliveryDate = Text(row, "supplyDeliveryDate"),
        fullTag = Text(row, "fullTag"),
        demandVer = Text(row, "demandVer"),
        scenarioId = Text(row, "scenarioId"),
        dataLayer = Text(row, "dataLayer"),
        evidenceType = Text(row, "evidenceType"),
        riskCode = Text(row, "riskCode"),
        riskLabel = Text(row, "riskLabel"),
        riskDetail = Text(row, "riskDetail"),
        evidenceRef = Text(row, "evidenceRef"),
        stockEligibleNum = Number(row, "stockEligibleNum"),
        grossStockNum = Number(row, "grossStockNum"),
        reservedByOtherNum = Number(row, "reservedByOtherNum"),
        qualityBlockedNum = Number(row, "qualityBlockedNum"),
        supplyOrderNum = Number(row, "supplyOrderNum"),
        supplyOrderOccupyNum = Number(row, "supplyOrderOccupyNum"),
        supplyOrderState = Text(row, "supplyOrderState"),
        supplyAuditTag = Text(row, "supplyAuditTag"),
        allocationStatus = Text(row, "allocationStatus"),
        allocationEvidence = Text(row, "allocationEvidence"),
        qualityState = Text(row, "qualityState"),
        qualityStateName = Text(row, "qualityStateName"),
        arrivalFeasible = Boolean(row, "arrivalFeasible"),
        arrivalStatus = Text(row, "arrivalStatus"),
        startDate = Text(row, "startDate"),
        lackNum = Number(row, "lackNum")
    };

    private static object Risk(JsonElement row) => new
    {
        scenarioId = Text(row, "scenarioId"),
        scenarioLabel = Text(row, "scenarioLabel"),
        riskCode = Text(row, "riskCode"),
        riskLabel = Text(row, "riskLabel"),
        riskDetail = Text(row, "riskDetail"),
        dataLayer = Text(row, "dataLayer"),
        evidenceRef = StringArray(row, "evidenceRef").Length > 0
            ? string.Join("；", StringArray(row, "evidenceRef"))
            : Text(row, "evidenceRef")
    };

    private static SummaryCounts BuildSummary(JsonElement[] orders, IReadOnlyDictionary<string, JsonElement> statuses, JsonElement[] readinessRows)
    {
        var total = orders.Length;
        var ready = 0;
        var notObtained = 0;
        var unknown = 0;
        foreach (var order in orders)
        {
            var id = Text(order, "orderId");
            statuses.TryGetValue(id, out var status);
            var key = OrderStatusKey(status, readinessRows.Where(x => Text(x, "orderId") == id).ToArray());
            if (key == "ready") ready++;
            else if (key == "not-obtained") notObtained++;
            else unknown++;
        }
        var obtained = ready + notObtained;
        var percent = total == 0 ? 0 : Math.Round((double)obtained / total * 100, 1);
        return new SummaryCounts(total, ready, notObtained, unknown, percent);
    }

    private static JsonElement[] SelectOrders(string scope, string orderId, string materialQuery, JsonElement[] orders, JsonElement[] bomRows, JsonElement[] readinessRows, JsonElement[] impactRows)
    {
        if (scope == "order") return orders.Where(x => string.Equals(Text(x, "orderId"), orderId, StringComparison.OrdinalIgnoreCase)).Take(1).ToArray();
        if (scope == "all") return orders;

        var matchedNos = MatchingMaterialNos(materialQuery, bomRows, readinessRows, impactRows);
        var impactOrderIds = impactRows
            .Where(x => matchedNos.Contains(Text(x, "materialNo"), StringComparer.OrdinalIgnoreCase) || MaterialMatches(Text(x, "materialNo"), "", materialQuery))
            .SelectMany(x => StringArray(x, "orderIds"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var evidenceOrderIds = readinessRows
            .Where(x => matchedNos.Contains(Text(x, "materialNo"), StringComparer.OrdinalIgnoreCase) || MaterialMatches(Text(x, "materialNo"), Text(x, "materialName"), materialQuery))
            .Select(x => Text(x, "orderId"))
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bomProductNos = bomRows
            .Where(x => matchedNos.Contains(Text(x, "materialNo"), StringComparer.OrdinalIgnoreCase))
            .Select(x => Text(x, "productNo"))
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return orders.Where(x =>
                impactOrderIds.Contains(Text(x, "orderId")) ||
                evidenceOrderIds.Contains(Text(x, "orderId")) ||
                bomProductNos.Contains(Text(x, "productNo")))
            .ToArray();
    }

    private static string[] MatchingMaterialNos(string query, JsonElement[] bomRows, JsonElement[] readinessRows, JsonElement[] impactRows)
        => bomRows.Concat(readinessRows).Concat(impactRows)
            .Where(x => MaterialMatches(Text(x, "materialNo"), Text(x, "materialName"), query))
            .Select(x => Text(x, "materialNo"))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static JsonElement[] RepresentativeOrders(JsonElement[] orders, IReadOnlyDictionary<string, JsonElement> statuses, int limit)
    {
        var result = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var grouped = new[] { "not-obtained", "ready", "unknown" }
            .ToDictionary(
                key => key,
                key => orders.Where(order =>
                {
                    var id = Text(order, "orderId");
                    statuses.TryGetValue(id, out var status);
                    return OrderStatusKey(status, Array.Empty<JsonElement>()) == key;
                }).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var positions = grouped.Keys.ToDictionary(key => key, _ => 0, StringComparer.OrdinalIgnoreCase);

        // Sample by status in rounds so a large F group cannot hide the T/unknown groups.
        while (result.Count < limit)
        {
            var addedInRound = false;
            foreach (var key in new[] { "not-obtained", "ready", "unknown" })
            {
                var candidates = grouped[key];
                if (positions[key] >= candidates.Length) continue;
                var order = candidates[positions[key]++];
                var id = Text(order, "orderId");
                if (!seen.Add(id)) continue;
                result.Add(order);
                addedInRound = true;
                if (result.Count >= limit) return result.ToArray();
            }
            if (!addedInRound) break;
        }
        foreach (var order in orders)
        {
            if (!seen.Add(Text(order, "orderId"))) continue;
            result.Add(order);
            if (result.Count >= limit) break;
        }
        return result.ToArray();
    }

    private static string BuildHeadline(string scope, string orderId, string materialQuery, SummaryCounts summary, int readinessResultRowCount)
    {
        if (scope == "order")
        {
            var status = summary.ReadyOrders == 1 ? "已有齐套标记" : summary.NotObtainedOrders == 1 ? "未获得齐套标记" : "齐套状态未取得";
            return $"订单 {orderId} {status}；本次快照取得 {readinessResultRowCount} 条物料结果。";
        }
        var prefix = scope == "material" ? $"物料“{materialQuery}”影响范围内" : "当前版本";
        return $"{prefix}共{summary.TotalOrders}个订单，{summary.ReadyOrders}个已有齐套标记，{summary.NotObtainedOrders}个未获得齐套标记，{summary.UnknownOrders}个状态未取得。";
    }

    private static string[] BuildLimitations(JsonElement snapshot)
    {
        var configured = Elements(snapshot, "limitations").Select(x => x.ToString()).Where(x => x.Length > 0).ToList();
        if (!configured.Any(x => x.Contains("fullTag", StringComparison.OrdinalIgnoreCase))) configured.Insert(0, "fullTag 是 APS 已有结果，本场景只展示，不在前端或应用层重新计算。");
        if (!configured.Any(x => x.Contains("未获得", StringComparison.Ordinal))) configured.Add("没有齐套结果的订单显示为状态未取得，不推断为短缺。");
        if (!configured.Any(x => x.Contains("只读", StringComparison.Ordinal))) configured.Add("全部数据来自内置演示快照，非实时查询，不执行重排、派单或下发。");
        return configured.ToArray();
    }

    private static object Metric(string label, int value, string unit, string tone) => new { label, value, unit, tone };

    private static string NormalizeScope(string scope)
        => (scope ?? "").Trim().ToLowerInvariant() switch
        {
            "订单" => "order",
            "物料" => "material",
            "全部" => "all",
            var value => value
        };

    private static string OrderStatusKey(JsonElement status, JsonElement[] readinessRows)
    {
        var raw = Text(status, "status").Trim().ToLowerInvariant();
        if (raw is "full_tag_t" or "t" or "ready" or "complete" || raw.Contains("full_tag_t")) return "ready";
        if (raw is "full_tag_f" or "f" || raw.Contains("full_tag_f")) return "not-obtained";
        if (raw.Contains("not_obtained") || raw.Contains("unknown")) return "unknown";
        if (readinessRows.Any(x => Text(x, "fullTag").Trim().Equals("T", StringComparison.OrdinalIgnoreCase))) return "ready";
        if (readinessRows.Any(x => Text(x, "fullTag").Trim().Equals("F", StringComparison.OrdinalIgnoreCase))) return "not-obtained";
        return "unknown";
    }

    private static string MaterialStatusKey(JsonElement row)
        => Text(row, "fullTag").Trim().ToUpperInvariant() switch
        {
            "T" => "ready",
            "F" => "not-obtained",
            _ => "unknown"
        };

    private static object StatusObject(string key) => key switch
    {
        "ready" => new { key, label = "已有齐套标记", shortLabel = "齐套", tone = "ready" },
        "not-obtained" => new { key, label = "未获得齐套标记", shortLabel = "未获得", tone = "not-obtained" },
        "not-applicable" => new { key, label = "不适用", shortLabel = "—", tone = "not-applicable" },
        _ => new { key = "unknown", label = "状态未取得", shortLabel = "未取得", tone = "unknown" }
    };

    private static void AddMaterial(List<(string No, string Name)> values, string materialNo, string materialName)
    {
        if (materialNo.Length == 0 || values.Any(x => string.Equals(x.No, materialNo, StringComparison.OrdinalIgnoreCase))) return;
        values.Add((materialNo, materialName));
    }

    private static bool ProductMatches(JsonElement row, string productNo)
        => string.Equals(Text(row, "productNo"), productNo, StringComparison.OrdinalIgnoreCase) || string.Equals(Text(row, "outputProductNo"), productNo, StringComparison.OrdinalIgnoreCase);

    private static bool IsDetailBom(JsonElement row)
        => Text(row, "bomLevel") != "001" && Text(row, "materialNo").Length > 0;

    private static bool MaterialMatches(string materialNo, string materialName, string query)
        => query.Length > 0 && (materialNo.Contains(query, StringComparison.OrdinalIgnoreCase) || materialName.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static string ShortMaterialName(string name, string materialNo)
    {
        var value = name.Length == 0 ? materialNo : name;
        foreach (var prefix in new[] { "磨工-", "热处理-", "车工-", "粗车-", "精车-" })
            if (value.StartsWith(prefix, StringComparison.Ordinal)) return prefix[..^1];
        return value;
    }

    private static string ObjectText(object value, string propertyName)
    {
        if (value is null) return "";
        var property = value.GetType().GetProperty(propertyName);
        return property?.GetValue(value)?.ToString() ?? "";
    }

    private static JsonElement[] Elements(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : Array.Empty<JsonElement>();

    private static string[] StringArray(JsonElement row, string name)
        => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(x => x.ToString()).Where(x => x.Length > 0).ToArray()
            : Array.Empty<string>();

    private static string Text(JsonElement row, string name)
        => row.ValueKind != JsonValueKind.Undefined && row.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";

    private static int Integer(JsonElement row, string name)
        => Number(row, name) is double value ? (int)value : 0;

    private static double? Number(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static bool? Boolean(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static ToolResult Fail(string summary, string error) => new() { Success = false, Summary = summary, ErrorMessage = error };

    private static JsonElement LoadSnapshot()
    {
        var assembly = typeof(BusinessMaterialReadiness).Assembly;
        using var stream = assembly.GetManifestResourceStream("WePilot.Agent.Data.production-demo.json")
            ?? throw new InvalidOperationException("Production demo snapshot missing.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("materialReadiness").Clone();
    }

    private sealed record SummaryCounts(int TotalOrders, int ReadyOrders, int NotObtainedOrders, int UnknownOrders, double CoveragePercent);
}
