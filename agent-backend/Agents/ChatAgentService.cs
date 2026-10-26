using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using WePilot.Agent.Contracts;
using WePilot.Agent.Infrastructure.Ai;
using WePilot.Agent.Infrastructure.Streaming;
using WePilot.Agent.Options;
using WePilot.Agent.Tools.Results;
using WePilot.Agent.Tools;

namespace WePilot.Agent.Agents;

public sealed class ChatAgentService
{
    private readonly KernelFactory _kernelFactory;
    private readonly IConfiguration _config;
    private readonly SampleTools _tools;
    private readonly ApsAnalysisTools _aps;
    private readonly BusinessOrderDispatch _orderDispatch;
    private readonly BusinessMaterialReadiness _materials;
    private readonly SseWriter _sseWriter;
    private readonly ToolExecutionContext _toolExecutionContext;
    private readonly AgentOptions _agentOptions;
    private readonly SemanticKernelOptions _skOptions;
    private readonly ILogger<ChatAgentService> _logger;

    public ChatAgentService(
        KernelFactory kernelFactory,
        IConfiguration config,
        SampleTools tools,
        ApsAnalysisTools aps,
        BusinessOrderDispatch orderDispatch,
        BusinessMaterialReadiness materials,
        SseWriter sseWriter,
        ToolExecutionContext toolExecutionContext,
        IOptionsMonitor<AgentOptions> agentOptions,
        IOptionsMonitor<SemanticKernelOptions> skOptions,
        ILogger<ChatAgentService> logger)
    {
        _kernelFactory = kernelFactory;
        _config = config;
        _tools = tools;
        _aps = aps;
        _orderDispatch = orderDispatch;
        _materials = materials;
        _sseWriter = sseWriter;
        _toolExecutionContext = toolExecutionContext;
        _agentOptions = agentOptions.CurrentValue;
        _skOptions = skOptions.CurrentValue;
        _logger = logger;
    }

    public async Task StreamAsync(ChatStreamRequest request, HttpResponse response, CancellationToken cancellationToken)
    {
        var requestId = response.HttpContext.TraceIdentifier;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(_skOptions.TimeoutMs, 1000, 300000)));
        var requestToken = deadline.Token;
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            await _sseWriter.WriteAsync(response, "error", new ErrorResponse
            {
                Code = "INVALID_REQUEST",
                Message = "请输入需要咨询的问题。",
                RequestId = requestId
            }, cancellationToken);
            return;
        }

        var startedAt = DateTimeOffset.Now;
        var answer = new StringBuilder();
        var productionNoticeWritten = false;
        int? firstTokenMs = null;

        _logger.LogInformation(
            "Agent request {RequestId} started for conversation {ConversationId}; historyMessages={HistoryMessageCount}.",
            requestId,
            request.ConversationId,
            request.History.Count);

        await _sseWriter.WriteAsync(response, "start", new
        {
            conversationId = request.ConversationId,
            requestId,
            agent = _agentOptions.Name,
            model = _skOptions.ModelId
        }, cancellationToken);

        try
        {
            _toolExecutionContext.RealSourceRequested = System.Text.RegularExpressions.Regex.IsMatch(request.Message, "真实|正式|实时|原系统|业务系统|业务库");
            _toolExecutionContext.DemoPlan = request.DemoPlan;
            _toolExecutionContext.InputMessage = request.Message;
            _toolExecutionContext.ConfirmedDemoAction = request.DemoAction;
            var localMode = (_config["Agent:Mode"] ?? "demo").Equals("demo", StringComparison.OrdinalIgnoreCase);
            var demoResult = request.DemoPlan?.SchemaVersion==4&&!_toolExecutionContext.RealSourceRequested?null:InteractiveDemoTools.Guard(request.Message);
            if (demoResult==null && localMode)
                demoResult = request.DemoAction is { } confirmed
                    ? new InteractiveDemoTools(_toolExecutionContext).Execute(confirmed.Action,confirmed.OrderId,confirmed.Position,confirmed.Quantity,confirmed.Scenario,request.DemoPlan,confirmed.Version)
                    : new InteractiveDemoTools(_toolExecutionContext).Route(request.Message, request.DemoPlan);
            if (demoResult != null)
            {
                await _sseWriter.WriteAsync(response, "delta", new { text = demoResult.Summary }, requestToken);
                if (demoResult.Success) await _sseWriter.WriteAsync(response, "ui_payload", new { blocks = demoResult.UiPayload }, requestToken);
                await _sseWriter.WriteAsync(response, "done", new { requestId, toolCount = _toolExecutionContext.ToolCalls.Count, toolCalls = _toolExecutionContext.ToolCalls, handledBy = "local-demo-router", modelInvoked = false, configuredMode = _config["Agent:Mode"] ?? "demo", textLength = demoResult.Summary.Length }, requestToken);
                return;
            }
            if ((_config["Agent:Mode"] ?? "demo").Equals("demo", StringComparison.OrdinalIgnoreCase))
            {
                var type = request.Message.Contains("line", StringComparison.OrdinalIgnoreCase) || request.Message.Contains("折线") ? "line"
                    : request.Message.Contains("pie", StringComparison.OrdinalIgnoreCase) || request.Message.Contains("饼") ? "pie" : "bar";
                var isProduction = System.Text.RegularExpressions.Regex.IsMatch(request.Message, "生产|排程|计划|产线|订单|日历|待排|未排|没排|排完整|交期|Z[0-9]{7,9}");
                var isOrderDispatch = System.Text.RegularExpressions.Regex.IsMatch(request.Message, "派工|形成派工|任务状态|当前任务|生成任务");
                var isMaterial = System.Text.RegularExpressions.Regex.IsMatch(request.Message, "齐套|哪些料|物料|库存|外购件|供应到货|MRP.*结果|已保存.*MRP|MRP.*需求");
                var isIncomplete = System.Text.RegularExpressions.Regex.IsMatch(request.Message, "没有完整排入|未完整排入|没有排完整|没排完整|未排完整|未排入|尚未排入|部分已排");
                var order = System.Text.RegularExpressions.Regex.Match(request.Message, @"Z[0-9]{7,9}\b").Value;
                var version = System.Text.RegularExpressions.Regex.Match(request.Message, @"\b\d{9}\b").Value;
                var materialQuery = ExtractMaterialQuery(request.Message);
                var materialScope = order.Length > 0 ? "order" : materialQuery.Length > 0 ? "material" : "all";
                var apsType = order.Length > 0 ? "order"
                    : request.Message.Contains("风险") || request.Message.Contains("延期") || request.Message.Contains("交期") || request.Message.Contains("按期") ? "risk" : "overview";
                var result = isMaterial
                    ? _materials.Execute(materialScope, order, materialScope == "material" ? materialQuery : "", version)
                    : isOrderDispatch
                    ? _orderDispatch.Execute(orderId: order)
                    : isProduction ? _aps.Execute(apsType, order, version: version,
                        source: request.Message.Contains("示例") || request.Message.Contains("合成") || request.Message.Contains("演示") ? "demo" : "",
                        scheduleScope: apsType == "overview" && isIncomplete ? "incomplete" : "all") : _tools.Execute(type);
                var demoNotice = "当前为受控演示模式，未调用大模型；以下结果由固定业务规则生成。\n\n";
                var text = isMaterial || isOrderDispatch || isProduction
                    ? demoNotice + result.Summary + (result.Success ? "请查看下方摘要；点击状态数量或“查看详细”核对记录。当前只读，不执行重排、派单或下发。" : result.ErrorMessage)
                    : demoNotice + "当前为固定示例数据。共 6 项，合计 **320**；当前结果不代表真实业务。";
                foreach (var character in text)
                {
                    await _sseWriter.WriteAsync(response, "delta", new { text = character.ToString() }, requestToken);
                    await Task.Delay(8, requestToken);
                }
                await _sseWriter.WriteAsync(response, "ui_payload", new { blocks = result.UiPayload }, requestToken);
                await _sseWriter.WriteAsync(response, "done", new { requestId, toolCount = _toolExecutionContext.ToolCalls.Count, toolCalls = _toolExecutionContext.ToolCalls, textLength = text.Length, mode = "demo" }, requestToken);
                return;
            }
            var kernel = _kernelFactory.CreateKernel();
            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var context = await PrepareContextAsync(request, kernel, requestToken);
            if (context.SummaryUpdated)
            {
                await _sseWriter.WriteAsync(response, "summary", new
                {
                    text = context.Summary,
                    summarizedMessageCount = context.SummarizedMessageCount
                }, cancellationToken);
            }

            var history = BuildHistory(request, context);

            var requireCurrentFacts=request.DemoPlan?.SchemaVersion==4&&System.Text.RegularExpressions.Regex.IsMatch(request.Message,@"订单|生产|排程|计划|交期|风险|延期|物料|材料|缺料|齐套|产线|工序|负载|库存|在途|MRP|需求|派工|任务|插单|试排|调序|安排|下发|下达|Z\d|C\.\d",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // Resolve current facts once. A rejected parameter may be corrected,
            // but a successful query must not turn into unsolicited extra queries.
            if(requireCurrentFacts)
            {
                var selection=await chat.GetChatMessageContentAsync(history,new OpenAIPromptExecutionSettings
                {
                    Temperature=_skOptions.Temperature,MaxTokens=_skOptions.MaxOutputTokens,
                    FunctionChoiceBehavior=FunctionChoiceBehavior.Required(autoInvoke:false)
                },kernel,requestToken);
                var calls=FunctionCallContent.GetFunctionCalls(selection).ToArray();
                if(calls.Length==0)throw new InvalidOperationException("Model did not select a current-facts tool.");
                if(request.DemoAction!=null&&calls.Length!=1)throw new InvalidOperationException("A confirmed demo request must select exactly one tool.");
                history.Add(selection);
                foreach(var call in calls)history.Add((await call.InvokeAsync(kernel,cancellationToken:requestToken)).ToChatMessage());
                if(request.DemoAction==null && _toolExecutionContext.UiPayload.Count==0)
                {
                    var retry=await chat.GetChatMessageContentAsync(history,new OpenAIPromptExecutionSettings
                    {
                        Temperature=_skOptions.Temperature,MaxTokens=_skOptions.MaxOutputTokens,
                        FunctionChoiceBehavior=FunctionChoiceBehavior.Auto(autoInvoke:false)
                    },kernel,requestToken);
                    history.Add(retry);
                    foreach(var call in FunctionCallContent.GetFunctionCalls(retry))
                        history.Add((await call.InvokeAsync(kernel,cancellationToken:requestToken)).ToChatMessage());
                }
            }
            var settings = new OpenAIPromptExecutionSettings
            {
                Temperature = _skOptions.Temperature,
                MaxTokens = _skOptions.MaxOutputTokens,
                FunctionChoiceBehavior = requireCurrentFacts || request.DemoAction!=null ? FunctionChoiceBehavior.None() : FunctionChoiceBehavior.Auto()
            };

            var checkNativeReply=request.DemoPlan?.SchemaVersion==4;
            var replyAdjusted=false;
            await foreach (var update in chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, requestToken))
            {
                if (string.IsNullOrEmpty(update.Content))
                {
                    continue;
                }

                firstTokenMs ??= (int)(DateTimeOffset.Now - startedAt).TotalMilliseconds;
                if(checkNativeReply){answer.Append(update.Content);continue;}
                if (!productionNoticeWritten && _toolExecutionContext.UiPayload.Any(block => block.Type is "production-plan" or "aps-analysis" or "order-dispatch" or "material-readiness" or "interactive-demo"))
                {
                    productionNoticeWritten = true;
                    var productionNotice = SourceNotice();
                    answer.Append(productionNotice);
                    await _sseWriter.WriteAsync(response, "delta", new { text = productionNotice }, cancellationToken);
                }
                answer.Append(update.Content);
                await _sseWriter.WriteAsync(response, "delta", new { text = update.Content }, cancellationToken);
            }

            if (request.DemoAction!=null && !_toolExecutionContext.UiPayload.Any(block => block.Type=="interactive-demo" &&
                System.Text.Json.JsonSerializer.SerializeToElement(block.Spec).TryGetProperty("view",out var view) && view.GetString()==request.DemoAction.Action))
            {
                await _sseWriter.WriteAsync(response,"error",new ErrorResponse {Code="DEMO_TOOL_NOT_EXECUTED",Message=_toolExecutionContext.ToolCalls.LastOrDefault(x=>!x.Success)?.Summary??"模型未完成已确认的演示运算；旧结果与当前方案保留，请重试。",RequestId=requestId},requestToken);
                return;
            }
            if(checkNativeReply)
            {
                var draft=answer.ToString();
                var presented=NativeReplyGuard.Present(draft,_toolExecutionContext.UiPayload,request.Message);
                replyAdjusted=presented!=draft;
                answer.Clear().Append(presented);
                await _sseWriter.WriteAsync(response,"delta",new{text=presented},requestToken);
            }
            if (_toolExecutionContext.UiPayload.Count > 0)
            {
                if (!productionNoticeWritten && _toolExecutionContext.UiPayload.Any(block => block.Type is "production-plan" or "aps-analysis" or "order-dispatch" or "material-readiness" or "interactive-demo"))
                {
                    var productionNotice = SourceNotice();
                    answer.Append(productionNotice);
                    await _sseWriter.WriteAsync(response, "delta", new { text = productionNotice }, cancellationToken);
                }
                await _sseWriter.WriteAsync(response, "ui_payload", new
                {
                    blocks = _toolExecutionContext.UiPayload
                }, cancellationToken);
            }

            await _sseWriter.WriteAsync(response, "done", new
            {
                elapsedMs = (int)(DateTimeOffset.Now - startedAt).TotalMilliseconds,
                textLength = answer.Length,
                toolCount = _toolExecutionContext.ToolCalls.Count,
                toolCalls = _toolExecutionContext.ToolCalls,
                modelInvoked = true,
                replyAdjusted,
                mode = "model",
                requestId
            }, cancellationToken);
            _logger.LogInformation(
                "Agent request {RequestId} completed for conversation {ConversationId}; firstTokenMs={FirstTokenMs}, elapsedMs={ElapsedMs}, toolCount={ToolCount}, textLength={TextLength}.",
                requestId,
                request.ConversationId,
                firstTokenMs,
                (int)(DateTimeOffset.Now - startedAt).TotalMilliseconds,
                _toolExecutionContext.ToolCalls.Count,
                answer.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Agent request {RequestId} for conversation {ConversationId} was cancelled by the client.", requestId, request.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent request {RequestId} for conversation {ConversationId} failed.", requestId, request.ConversationId);
            if (!cancellationToken.IsCancellationRequested)
            {
                await _sseWriter.WriteAsync(response, "error", new ErrorResponse
                {
                    Code = ex is OperationCanceledException ? "AGENT_TIMEOUT" : "AGENT_UPSTREAM_ERROR",
                    Message = ex is OperationCanceledException
                        ? "智能体响应超时，请稍后重试。"
                        : "智能体服务暂时不可用，请稍后重试。",
                    RequestId = requestId
                }, cancellationToken);
            }
        }
    }

    private string SourceNotice()
    {
        var sources = _toolExecutionContext.UiPayload.Where(x => x.Type is "aps-analysis" or "order-dispatch" or "material-readiness" or "interactive-demo")
            .Select(x => System.Text.Json.JsonSerializer.SerializeToElement(x.Spec))
            .Select(x => x.TryGetProperty("source", out var s) ? s.GetString() : "synthetic-demo").Distinct().ToArray();
        if(sources.Contains("isolated-demo")) return "";
        return sources.Length > 1 ? "\n\n本次包含不同来源的查询结果，请分别核对。\n\n"
            : sources.Contains("business-snapshot") ? "\n\n数据来自已保存的业务快照。\n\n" : "";
    }

    private static string ExtractMaterialQuery(string message)
    {
        var quoted = System.Text.RegularExpressions.Regex.Match(message, "[\\\"“](.+?)[\\\"”]").Groups[1].Value.Trim();
        if (quoted.Length > 0) return quoted;
        return System.Text.RegularExpressions.Regex.Match(message, @"C\.\d{2}\.\d{2}\.\d{4}|[A-Za-z]{2,}\d(?:[-_][A-Za-z0-9]+)*|钢材|轴承钢|外购件").Value;
    }

    private async Task<ContextWindow> PrepareContextAsync(ChatStreamRequest request, Kernel kernel, CancellationToken cancellationToken)
    {
        var messages = request.History
            .Where(item => !string.IsNullOrWhiteSpace(item.Content) && IsSupportedRole(item.Role))
            .Select(item => new ChatMessageDto { Role = item.Role.ToLowerInvariant(), Content = item.Content.Trim() })
            .ToList();
        var summarizedCount = Math.Clamp(request.SummarizedMessageCount, 0, messages.Count);
        var summary = request.Summary?.Trim() ?? "";
        var compressCount = ContextCompressionPolicy.GetCompressMessageCount(
            messages.Count,
            summarizedCount,
            _agentOptions.SummaryTriggerRounds,
            _agentOptions.SummaryKeepRecentRounds);
        var summaryUpdated = false;

        if (compressCount > 0)
        {
            var toCompress = messages.Skip(summarizedCount).Take(compressCount).ToList();
            try
            {
                var updated = await SummarizeAsync(summary, toCompress, kernel, cancellationToken);
                if (!string.IsNullOrWhiteSpace(updated))
                {
                    summary = updated.Trim();
                    summarizedCount += compressCount;
                    summaryUpdated = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Conversation {ConversationId} summary update failed; continuing with recent messages.", request.ConversationId);
            }
        }

        var maxMessages = Math.Max(2, _agentOptions.HistoryMaxRounds * 2);
        var recent = messages.Skip(summarizedCount).TakeLast(maxMessages).ToList();
        return new ContextWindow(summary, summarizedCount, recent, summaryUpdated);
    }

    private async Task<string> SummarizeAsync(string existingSummary, IReadOnlyCollection<ChatMessageDto> messages, Kernel kernel, CancellationToken cancellationToken)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var prompt = new ChatHistory(
            "你负责压缩助手的历史对话。仅保留用户目标、已确认条件、关键对象、时间范围、工具事实、重要结论和未解决问题。" +
            "不要添加原对话中不存在的信息，不要保留寒暄、重复表达或无意义的语音识别噪声。输出简洁中文纯文本摘要。");
        var content = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(existingSummary))
        {
            content.AppendLine("已有摘要：").AppendLine(existingSummary).AppendLine();
        }
        content.AppendLine("需要并入摘要的新对话：");
        foreach (var item in messages)
        {
            content.Append(item.Role == "assistant" ? "助手：" : "用户：").AppendLine(item.Content);
        }
        prompt.AddUserMessage(content.ToString());
        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0,
            MaxTokens = Math.Max(100, _agentOptions.SummaryMaxOutputTokens)
        };
        var result = await chat.GetChatMessageContentAsync(prompt, settings, kernel, cancellationToken);
        return result.Content ?? "";
    }

    private ChatHistory BuildHistory(ChatStreamRequest request, ContextWindow context)
    {
        var prompt = string.IsNullOrWhiteSpace(_agentOptions.SystemPrompt)
            ? AgentPrompts.DefaultSystemPrompt
            : _agentOptions.SystemPrompt;

        var history = new ChatHistory(request.DemoPlan?.SchemaVersion==4 ? AgentPrompts.NativeSystemPrompt : prompt);
        if (request.DemoAction!=null)
        {
            var function=request.DemoAction.Action switch {"insert"=>"simulate_insert_demo","mrp"=>"calculate_mrp_demo","reschedule"=>"simulate_reschedule_demo","release"=>"release_demo_plan",_=>""};
            history.AddSystemMessage(function.Length>0
                ? $"本请求用户已在页面确认本地模拟输入。现在只调用一次 {function}，不要调用准备工具或其他查询工具。完整确认输入由服务端保管，你无须且不能重填订单、数量、顺序、供应或版本。根据工具摘要解释结果；不能声明已采用、采购已下单或原系统保存成功。"
                : "本请求的确认动作无效，不执行任何工具，说明输入错误。");
        }
        else
            history.AddSystemMessage("本请求没有页面计算确认。新增急单插入先调用prepare_insert_demo，已有订单调序先调用prepare_reschedule_demo，模拟MRP/计算材料需求先调用prepare_mrp_demo，模拟下达先调用prepare_release_demo；没有界面确认不能调用计算或下达工具。没有指定订单时传空字符串，使用工具确认卡供用户选择，不自行选单。供应和需求数量按用户原话提取，不把未知或场景外值替换成默认。");
        if (request.DemoPlan?.SchemaVersion==4)
            history.AddSystemMessage($"当前工作区有{request.DemoPlan.OrderIds.Length}张订单：{string.Join('/',request.DemoPlan.OrderIds)}。下发状态：{(request.DemoPlan.ReleaseStatus=="released"?"已下发":"未下发")}。内部来源和计算结果由请求保管，查询自动使用当前已采用方案，已有物料核算结果由物料工具读取。尚未加入当前计划、可以准备插入的订单：{string.Join("；",NativeDemoWorkspace.InsertionOrders.Where(o=>!request.DemoPlan.OrderIds.Contains(o.GetProperty("orderId").GetString())).Select(o=>$"{o.GetProperty("orderId").GetString()}，产品{o.GetProperty("productNo").GetString()}、{o.GetProperty("quantity").GetDouble()}件、交期{o.GetProperty("dueDate").GetString()}"))}。名单为空时没有新的待插入订单。用户未指定订单时仍传空字符串，由确认卡选择，不能自行选单。未加入计划的订单须先准备插入，不能说它已经在当前批次。不把内部上下文和工具名称复述给用户。只根据本次工具事实作答，不凭历史补数字。计算、采用和下发分为不同阶段，不能提前声明完成。未提供的负载数据或日期视图不许声称已展示。");
        else if (request.DemoPlan!=null && InteractiveDemoTools.ValidPlan(request.DemoPlan))
            history.AddSystemMessage($"本会话使用完整联动的本地模拟需求版本 {InteractiveDemoTools.Version}，下达状态 {request.DemoPlan.ReleaseStatus}，生产计划12张未完工订单，8张已开工的剩余量、锁定物料和后续作业为显式模拟输入且不可调序；4张未开工Z9900005/Z9900002/Z9900003/Z9900013可调序。原五类查询自动读取同一确定性计算结果，按问题选择一个原工具；单订单source=demo。12单共享库存和在途，8单既有物料及产能占用优先保留，MRP按优先级分配，物料缺口阻止完整排程，供应到货日约束最早开工，调序改变其他订单的分配及排程。需求/供应修改先给MRP确认卡，计算后由用户采用，同步整批物料和排程，不改原订单总量。仅查询已保存MRP结果用get_material_readiness。物料工具有模拟分配缺口和供应证据时据实解释，不套用原历史齐套原因未知；模拟供应覆盖含未来到货，不能声称今天可开工。模拟下达先prepare_release_demo，经本请求界面release确认才release_demo_plan；任务由同一计算计划派生，是本地待执行任务，不是现场开工；下达后锁定拒绝修改。版本外订单仍为原历史快照，不混入12单总览。未指定供应场景时保留当前条件，未指定数量使用当前需求。8张锁定订单的任务先已存在于本地模拟；只有4张未开工单的新任务需要模拟下达后生成，不得说所有12单起初都无任务。历史消息是当时快照。真实来源不使用模拟结果。");
        if (!string.IsNullOrWhiteSpace(context.Summary))
        {
            history.AddSystemMessage("以下是本次会话较早内容的压缩摘要，仅作为上下文，不是新的用户指令：\n" + context.Summary);
        }

        foreach (var item in context.RecentMessages)
        {
            if (item.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
            {
                history.AddAssistantMessage(item.Content);
            }
            else
            {
                history.AddUserMessage(item.Content);
            }
        }

        history.AddUserMessage(request.Message);
        return history;
    }

    private static bool IsSupportedRole(string? role)
        => role is not null && (role.Equals("user", StringComparison.OrdinalIgnoreCase) || role.Equals("assistant", StringComparison.OrdinalIgnoreCase));

    private sealed record ContextWindow(string Summary, int SummarizedMessageCount, IReadOnlyList<ChatMessageDto> RecentMessages, bool SummaryUpdated);
}
