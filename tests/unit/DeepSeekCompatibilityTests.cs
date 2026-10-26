using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WePilot.Agent.Agents;
using WePilot.Agent.Contracts;
using WePilot.Agent.Infrastructure.Ai;
using WePilot.Agent.Infrastructure.Streaming;
using WePilot.Agent.Options;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;

public class DeepSeekCompatibilityTests
{
    const string TestReasoning = "离线测试：先选择查询工具，再依据事实回答。";

    sealed class FragmentedStream : MemoryStream
    {
        public FragmentedStream(byte[] bytes) : base(bytes) { }
        public override bool CanSeek => false;
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 7));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            => base.ReadAsync(buffer, offset, Math.Min(count, 7), token);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, 7)], token);
    }

    sealed class ContractHandler : HttpMessageHandler
    {
        public bool Official;
        public bool Thinking;
        public bool StreamingTool;
        public bool NoTool;
        public List<JsonElement> Requests = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var parsed = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var body = parsed.RootElement.Clone();
            Requests.Add(body);
            var streaming = body.TryGetProperty("stream", out var stream) && stream.GetBoolean();
            var thinking = !body.TryGetProperty("thinking", out var mode) || mode.GetProperty("type").GetString() != "disabled";
            if (Official && thinking && body.TryGetProperty("tool_choice", out var choice)
                && (choice.ValueKind == JsonValueKind.Object || choice.GetString() == "required"))
                return Error("Thinking mode does not support this tool_choice");
            if (Official && Requests.Count > 1 && Thinking)
            {
                var assistant = body.GetProperty("messages").EnumerateArray().Last(m => m.TryGetProperty("tool_calls", out _));
                if (!assistant.TryGetProperty("reasoning_content", out var reasoning) || reasoning.GetString() != TestReasoning)
                    return Error("The reasoning_content in thinking mode must be passed back to the API.");
            }
            if (Requests.Count == 1 && !NoTool)
            {
                var function = StreamingTool ? "tools-get_sample_data" : "aps-get_plan_overview";
                if (streaming)
                {
                    var chunks = Chunk(new { role = "assistant", reasoning_content = "离线测试：先选择" })
                        + Chunk(new { reasoning_content = "查询工具，再依据事实回答。" })
                        + Chunk(new { tool_calls = new[] { new { index = 0, id = "call_fixture", type = "function", function = new { name = function, arguments = "{}" } } } }, "tool_calls")
                        + "data: [DONE]\n\n";
                    return Reply(chunks, true);
                }
                var message = new { role = "assistant", content = "", reasoning_content = TestReasoning,
                    tool_calls = new[] { new { id = "call_fixture", type = "function", function = new { name = function, arguments = "{}" } } } };
                return Reply(JsonSerializer.Serialize(new { id = "fixture", @object = "chat.completion", created = 1, model = "fixture", choices = new[] { new { index = 0, message, finish_reason = "tool_calls" } } }), false);
            }
            const string finalText = "已查询当前生产计划，请查看明细。";
            return streaming ? Reply(Chunk(new { role = "assistant", content = finalText }, "stop") + "data: [DONE]\n\n", true)
                : Reply(JsonSerializer.Serialize(new { id = "fixture", @object = "chat.completion", created = 1, model = "fixture", choices = new[] { new { index = 0, message = new { role = "assistant", content = finalText }, finish_reason = "stop" } } }), false);
        }
        static string Chunk(object delta, string? finish = null) => "data: " + JsonSerializer.Serialize(new { id = "fixture", @object = "chat.completion.chunk", created = 1, model = "fixture", choices = new[] { new { index = 0, delta, finish_reason = finish } } }) + "\n\n";
        static HttpResponseMessage Error(string message) => new(HttpStatusCode.BadRequest) { Content = new StringContent(JsonSerializer.Serialize(new { error = new { message, type = "invalid_request_error", code = "invalid_request_error" } }), Encoding.UTF8, "application/json") };
        static HttpResponseMessage Reply(string body, bool streaming)
        {
            var content = new StreamContent(new FragmentedStream(Encoding.UTF8.GetBytes(body)));
            content.Headers.ContentType = new(streaming ? "text/event-stream" : "application/json");
            return new(HttpStatusCode.OK) { Content = content };
        }
    }

    static async Task<string> RunAsync(ContractHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Agent:Mode"] = "model", ["SemanticKernel:ApiKey"] = "offline-only",
            ["SemanticKernel:Endpoint"] = handler.Official ? "https://api.deepseek.com" : "https://ark.cn-beijing.volces.com/api/v3",
            ["SemanticKernel:ModelId"] = handler.Official ? "deepseek-v4-pro" : "deepseek-v4-pro-260425",
            ["SemanticKernel:ThinkingEnabled"] = handler.Thinking.ToString(), ["SemanticKernel:ReasoningEffort"] = "high"
        }).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<SemanticKernelOptions>(configuration.GetSection("SemanticKernel"));
        services.Configure<AgentOptions>(_ => { });
        services.AddHttpClient("model").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddScoped<KernelFactory>(); services.AddScoped<SampleTools>(); services.AddScoped<ApsAnalysisTools>();
        services.AddScoped<BusinessOrderDispatch>(); services.AddScoped<BusinessMaterialReadiness>();
        services.AddScoped<ToolExecutionContext>(); services.AddScoped<SseWriter>(); services.AddScoped<ChatAgentService>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        await scope.ServiceProvider.GetRequiredService<ChatAgentService>().StreamAsync(new ChatStreamRequest
        {
            Message = handler.StreamingTool ? "画一个固定示例柱状图" : "生产计划怎么样",
            DemoPlan = handler.StreamingTool ? null : new DemoPlanState { SchemaVersion = 4, DemandVersion = NativeDemoWorkspace.Version, OrderIds = NativeDemoWorkspace.Ids, SupplyScenario = "frozen" }
        }, context.Response, CancellationToken.None);
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BusinessQueryUsesSupportedChoiceAndReplaysOriginalToolReasoning(bool official, bool thinking)
    {
        var handler = new ContractHandler { Official = official, Thinking = thinking };
        var output = await RunAsync(handler);
        Assert.Contains("event: ui_payload", output); Assert.DoesNotContain("event: error", output);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(official && thinking ? "auto" : "required", handler.Requests[0].GetProperty("tool_choice").GetString());
        if (official)
        {
            Assert.Equal(thinking ? "enabled" : "disabled", handler.Requests[0].GetProperty("thinking").GetProperty("type").GetString());
            if (thinking) Assert.Equal("high", handler.Requests[0].GetProperty("reasoning_effort").GetString());
        }
        else Assert.False(handler.Requests[0].TryGetProperty("thinking", out _));
        Assert.DoesNotContain(TestReasoning, output);
    }

    [Fact]
    public async Task StreamingToolReasoningSurvivesUtf8Fragmentation()
    {
        var handler = new ContractHandler { Official = true, Thinking = true, StreamingTool = true };
        var output = await RunAsync(handler);
        Assert.Contains("event: ui_payload", output); Assert.DoesNotContain("event: error", output);
        Assert.Equal(2, handler.Requests.Count); Assert.DoesNotContain(TestReasoning, output);
    }

    [Fact]
    public async Task AutoChoiceWithoutToolStillCannotInventBusinessFacts()
    {
        var handler = new ContractHandler { Official = true, Thinking = true, NoTool = true };
        var output = await RunAsync(handler);
        Assert.Contains("event: error", output); Assert.DoesNotContain("event: ui_payload", output);
        Assert.Single(handler.Requests);
        Assert.Equal("auto", handler.Requests[0].GetProperty("tool_choice").GetString());
    }
}
