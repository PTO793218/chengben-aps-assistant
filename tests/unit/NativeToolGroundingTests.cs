using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WePilot.Agent.Agents;
using WePilot.Agent.Contracts;
using WePilot.Agent.Infrastructure.Ai;
using WePilot.Agent.Infrastructure.Streaming;
using WePilot.Agent.Options;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;
public class NativeToolGroundingTests
{
    sealed class ReplyStream:MemoryStream { public ReplyStream(byte[] bytes):base(bytes){} public override bool CanSeek=>false; }
    sealed class ModelHandler:HttpMessageHandler
    {
        public bool CorrectionToolsAvailable;
        public bool InitiallyValid;
        int calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var b=body.RootElement;var streaming=b.TryGetProperty("stream",out var stream)&&stream.GetBoolean();calls++;
            object? tool=null;string? content=null;
            if(calls==1)tool=new[]{new{index=0,id="call1",type="function",function=new{name="materials-get_material_readiness",arguments=InitiallyValid?"{\"scope\":\"order\",\"orderId\":\"Z9900006\"}":"{\"scope\":\"order\",\"orderId\":\"Z9900006\",\"version\":\"old-version\"}"}}};
            else if(calls==2)
            {
                // SK may retain tool schemas with tool_choice=none; availability
                // is the protocol choice, not just presence of the schemas.
                CorrectionToolsAvailable=b.TryGetProperty("tools",out var tools)&&tools.GetArrayLength()>0
                    && !(b.TryGetProperty("tool_choice",out var choice)&&choice.ValueKind==JsonValueKind.String&&choice.GetString()=="none");
                if(CorrectionToolsAvailable)tool=new[]{new{index=0,id="call2",type="function",function=new{name="materials-get_material_readiness",arguments="{\"scope\":\"order\",\"orderId\":\"Z9900006\"}"}}};
                else content="工具不可用";
            }
            else content="已重新查询当前物料依据。";
            var envelope=streaming
                ?JsonSerializer.Serialize(new{id="test",@object="chat.completion.chunk",created=1,model="test",choices=new[]{new{index=0,delta=new{role="assistant",content,tool_calls=tool},finish_reason=tool==null?"stop":"tool_calls"}}},new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull})
                :JsonSerializer.Serialize(new{id="test",@object="chat.completion",created=1,model="test",choices=new[]{new{index=0,message=new{role="assistant",content,tool_calls=tool},finish_reason=tool==null?"stop":"tool_calls"}}});
            var reply=new StreamContent(new ReplyStream(Encoding.UTF8.GetBytes(streaming?"data: "+envelope+"\n\ndata: [DONE]\n\n":envelope)));
            reply.Headers.ContentType=new(streaming?"text/event-stream":"application/json");
            return new(HttpStatusCode.OK){Content=reply};
        }
    }
    [Theory]
    [InlineData(false,2,true)]
    [InlineData(true,1,false)]
    public async Task CorrectRejectedParametersButStopAfterTheCurrentQuestionIsAnswered(bool initiallyValid,int expectedCalls,bool correctionAllowed)
    {
        var handler=new ModelHandler{InitiallyValid=initiallyValid};var services=new ServiceCollection();services.AddLogging(b=>b.AddConsole());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string>{{"Agent:Mode","model"}}).Build());
        services.Configure<SemanticKernelOptions>(o=>{o.Endpoint="https://model.invalid/v1";o.ApiKey="test-only";o.ModelId="test";});services.Configure<AgentOptions>(_=>{});
        services.AddHttpClient("model").ConfigurePrimaryHttpMessageHandler(()=>handler);
        services.AddScoped<KernelFactory>();services.AddScoped<SampleTools>();services.AddScoped<ApsAnalysisTools>();services.AddScoped<BusinessOrderDispatch>();services.AddScoped<BusinessMaterialReadiness>();services.AddScoped<ToolExecutionContext>();services.AddScoped<SseWriter>();services.AddScoped<ChatAgentService>();
        using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
        var p=new DemoPlanState{SchemaVersion=4,DemandVersion=NativeDemoWorkspace.Version,OrderIds=NativeDemoWorkspace.Ids,SupplyScenario="frozen"};
        var context=new DefaultHttpContext();context.Response.Body=new MemoryStream();
        await scope.ServiceProvider.GetRequiredService<ChatAgentService>().StreamAsync(new ChatStreamRequest{Message="查询演示订单Z9900006的物料",DemoPlan=p},context.Response,CancellationToken.None);
        context.Response.Body.Position=0;var output=await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(correctionAllowed,handler.CorrectionToolsAvailable);
        Assert.True(output.Contains("event: ui_payload"),output);Assert.DoesNotContain("event: error",output);
        Assert.Equal(expectedCalls,scope.ServiceProvider.GetRequiredService<ToolExecutionContext>().ToolCalls.Count);
    }
}
