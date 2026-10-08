using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using WePilot.Agent.Agents;
using WePilot.Agent.Contracts;
using WePilot.Agent.Infrastructure.Ai;
using WePilot.Agent.Infrastructure.Streaming;
using WePilot.Agent.Options;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;
namespace WePilot.Tests;
public sealed class DemoStreamTests
{
    [Fact]
    public async Task DemoModeProducesTextAndUiWithoutCredentials()
    {
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Agent:Mode","demo"}}).Build());
        services.Configure<SemanticKernelOptions>(_=>{});services.Configure<AgentOptions>(_=>{});
        services.AddHttpClient();services.AddScoped<KernelFactory>();services.AddScoped<SampleTools>();
        services.AddScoped<ProductionTools>();
        services.AddScoped<ApsAnalysisTools>();
        services.AddScoped<BusinessOrderDispatch>();
        services.AddScoped<BusinessMaterialReadiness>();
        services.AddScoped<ToolExecutionContext>();services.AddScoped<SseWriter>();services.AddScoped<ChatAgentService>();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
        var context=new DefaultHttpContext();context.Response.Body=new MemoryStream();
        await scope.ServiceProvider.GetRequiredService<ChatAgentService>().StreamAsync(new ChatStreamRequest{Message="折线图"},context.Response,CancellationToken.None);
        context.Response.Body.Position=0;var output=await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("event: delta",output);Assert.Contains("event: ui_payload",output);Assert.Contains("\"chartType\":\"line\"",output);Assert.Contains("event: done",output);Assert.DoesNotContain("event: error",output);
    }

    [Fact]
    public async Task DemoModeRoutesNaturalLanguageOrderDispatchQuestionToControlledSnapshot()
    {
        var snapshot = Path.GetTempFileName();
        try
        {
            File.WriteAllText(snapshot, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, source = "business-snapshot", systemNo = "731", asOf = "2026-09-17T10:00:00+08:00",
                plans = new[] { new { planGuid="P1", planType="normal", planNo="PL-1", productNo="A", productName="产品A", lineId="L1", planStart="2026-09-01", planEnd="2026-09-03", planQuantity=100 } },
                currentTasks = new[] { new { taskGuid="T1", pdPlanGuid="P1", taskNo="TK-1", productNo="A", productName="产品A", lineId="L1", taskStart="2026-09-01", taskEnd="2026-09-02", taskQuantity=100, status="run" } },
                latestTasks = Array.Empty<object>()
            }));
            var services = new ServiceCollection(); services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>
            {
                {"Agent:Mode","demo"}, {"BusinessData:PlanTaskSnapshotPath",snapshot}
            }).Build());
            services.Configure<SemanticKernelOptions>(_=>{}); services.Configure<AgentOptions>(_=>{});
            services.AddHttpClient(); services.AddScoped<KernelFactory>(); services.AddScoped<SampleTools>(); services.AddScoped<ProductionTools>();
            services.AddScoped<ApsAnalysisTools>(); services.AddScoped<BusinessOrderDispatch>(); services.AddScoped<BusinessMaterialReadiness>(); services.AddScoped<ToolExecutionContext>();
            services.AddScoped<SseWriter>(); services.AddScoped<ChatAgentService>();
            using var provider=services.BuildServiceProvider(); using var scope=provider.CreateScope();
            var context=new DefaultHttpContext(); context.Response.Body=new MemoryStream();
            await scope.ServiceProvider.GetRequiredService<ChatAgentService>().StreamAsync(new ChatStreamRequest{Message="哪些订单已经形成派工？"},context.Response,CancellationToken.None);
            context.Response.Body.Position=0; var output=await new StreamReader(context.Response.Body).ReadToEndAsync();
            Assert.Contains("\"type\":\"order-dispatch\"", output);
            Assert.Contains("\"toolName\":\"get_order_dispatch_status\"", output);
            var streamedText = string.Concat(output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("data: {\"text\":", StringComparison.Ordinal))
                .Select(line => JsonDocument.Parse(line["data: ".Length..].Trim()).RootElement.GetProperty("text").GetString()));
            Assert.StartsWith("当前为受控演示模式，未调用大模型；以下结果由固定业务规则生成。", streamedText);

            async Task<string> Ask(string message)
            {
                using var askScope = provider.CreateScope();
                var askContext = new DefaultHttpContext();
                askContext.Response.Body = new MemoryStream();
                await askScope.ServiceProvider.GetRequiredService<ChatAgentService>()
                    .StreamAsync(new ChatStreamRequest { Message = message }, askContext.Response, CancellationToken.None);
                askContext.Response.Body.Position = 0;
                return await new StreamReader(askContext.Response.Body).ReadToEndAsync();
            }

            var incomplete = await Ask("哪些订单还没有完整排入计划？");
            Assert.Contains("\"scheduleScope\":\"incomplete\"", incomplete);
            using var incompletePayload = JsonDocument.Parse(incomplete.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith("data: {\"blocks\":", StringComparison.Ordinal))["data: ".Length..].Trim());
            Assert.Equal(4, incompletePayload.RootElement.GetProperty("blocks")[0].GetProperty("spec").GetProperty("detail").GetProperty("rows").GetArrayLength());

            var oneOrder = await Ask("订单 Z9900001 派工了吗？");
            Assert.Contains("\"toolName\":\"get_order_dispatch_status\"", oneOrder);
            Assert.Contains("\"orderId\":\"Z9900001\"", oneOrder);
            using var dispatchPayload = JsonDocument.Parse(oneOrder.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith("data: {\"blocks\":", StringComparison.Ordinal))["data: ".Length..].Trim());
            Assert.Single(dispatchPayload.RootElement.GetProperty("blocks")[0].GetProperty("spec").GetProperty("orderRows").EnumerateArray());
        }
        finally { File.Delete(snapshot); }
    }
}
