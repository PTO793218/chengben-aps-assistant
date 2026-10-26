using System.Text.Json;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools;
using WePilot.Agent.Tools.Results;
using Xunit;

namespace WePilot.Tests;
public sealed class InteractiveDemoModelToolTests
{
    static object Plugin(ToolExecutionContext context)
    {
        var type=typeof(InteractiveDemoTools).Assembly.GetType("WePilot.Agent.Tools.InteractiveDemoModelTools");
        Assert.NotNull(type);
        return Activator.CreateInstance(type!,context)!;
    }
    static ToolResult Call(object plugin,string method,params object?[] args)=>ToolResult.FromModelJson((string)plugin.GetType().GetMethod(method)!.Invoke(plugin,args)!);
    static void Confirm(ToolExecutionContext context,string json)
    {
        var type=typeof(InteractiveDemoTools).Assembly.GetType("WePilot.Agent.Contracts.ConfirmedDemoAction")!;
        typeof(ToolExecutionContext).GetProperty("ConfirmedDemoAction")!.SetValue(context,JsonSerializer.Deserialize(json,type,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [Fact] public void ModelUsesSixActionFunctionsAndExistingQueryTools()
    {
        var plugin=Plugin(new());var kernel=Kernel.CreateBuilder().Build();kernel.Plugins.AddFromObject(plugin,"demos");
        foreach(var name in new[]{"prepare_reschedule_demo","prepare_mrp_demo","simulate_reschedule_demo","calculate_mrp_demo","prepare_release_demo","release_demo_plan"})
            Assert.True(kernel.Plugins.TryGetFunction("demos",name,out _),name);
        Assert.False(kernel.Plugins.TryGetFunction("demos","get_current_demo_schedule",out _));
    }
    [Fact] public void ModelCannotCalculateWithoutUiConfirmation()
    {
        var context=new ToolExecutionContext();var plugin=Plugin(context);
        Assert.False(Call(plugin,"CalculateMrpDemo").Success);Assert.Empty(context.UiPayload);
        Assert.False(Call(plugin,"SimulateRescheduleDemo").Success);Assert.Empty(context.UiPayload);
    }
    [Fact] public void ConfirmedInputComesFromRequestAndFullUiStaysOutOfModel()
    {
        var context=new ToolExecutionContext();var plugin=Plugin(context);
        Confirm(context,"{\"action\":\"mrp\",\"orderId\":\"Z9900003\",\"quantity\":8000,\"scenario\":\"supply\",\"version\":\"SIM-20261015\"}");
        var modelResult=Call(plugin,"CalculateMrpDemo");Assert.True(modelResult.Success);Assert.DoesNotContain("uiPayload",modelResult.ToModelJson());
        var spec=JsonSerializer.SerializeToElement(context.UiPayload.Single().Spec,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(8000,spec.GetProperty("quantity").GetInt32());Assert.Equal(0m,spec.GetProperty("purchaseNeed").GetDecimal());
        Assert.Equal("calculate_mrp_demo",context.ToolCalls.Single().ToolName);
    }
    [Fact] public void RealSourceAndMismatchedConfirmedActionAreRejected()
    {
        var context=new ToolExecutionContext{RealSourceRequested=true};var plugin=Plugin(context);
        Confirm(context,"{\"action\":\"mrp\",\"orderId\":\"Z9900003\",\"quantity\":8000,\"scenario\":\"none\"}");
        Assert.False(Call(plugin,"CalculateMrpDemo").Success);Assert.Empty(context.UiPayload);
        context.RealSourceRequested=false;
        Assert.False(Call(plugin,"SimulateRescheduleDemo").Success);Assert.Empty(context.UiPayload);
    }
    [Fact] public void ExplicitSupplyAmountsCannotContradictScenario()
    {
        foreach(var item in new[]{("supply",0m,0m),("none",1000m,1500m)})
        {
            var context=new ToolExecutionContext();var plugin=Plugin(context);
            Assert.False(Call(plugin,"PrepareMrpDemo","Z9900003",8000,item.Item1,item.Item2,item.Item3).Success);
            Assert.Empty(context.UiPayload);
        }
    }
    [Fact] public void CurrentDemandNormalizesModelProvidedOrderNumber()
    {
        var context=new ToolExecutionContext{DemoPlan=new(){DemandQuantities=new(){{"Z9900003",8000}}}};var plugin=Plugin(context);
        Assert.True(Call(plugin,"PrepareMrpDemo"," z9900003 ",null,"",null,null).Success);
        var spec=JsonSerializer.SerializeToElement(context.UiPayload.Single().Spec,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(8000,spec.GetProperty("quantity").GetInt32());
    }
    [Fact] public void DefaultDemandNormalizesModelProvidedOrderNumber()
    {
        var context=new ToolExecutionContext();var plugin=Plugin(context);
        Assert.True(Call(plugin,"PrepareMrpDemo"," z9900013 ",null,"none",null,null).Success);
        var spec=JsonSerializer.SerializeToElement(context.UiPayload.Single().Spec,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(50000,spec.GetProperty("quantity").GetInt32());
    }
}

