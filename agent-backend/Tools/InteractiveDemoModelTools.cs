using System.ComponentModel;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

// Model-facing functions; all dates and quantities still come from the deterministic engine.
public sealed class InteractiveDemoModelTools
{
    private readonly ToolExecutionContext _context;
    private readonly InteractiveDemoTools _engine;
    public InteractiveDemoModelTools(ToolExecutionContext context) { _context=context; _engine=new(context); }

    [KernelFunction("prepare_reschedule_demo")]
    [Description("用户明确要求演示/模拟插队、提前安排、调顺序或重排时调用。只生成当前会话同批订单的输入确认卡，尚不计算、不采用；没有订单号就留空供用户选择，不能自行选择。")]
    public string PrepareRescheduleDemo(
        [Description("用户指定的完整订单号；未指定留空；不要改成其他单。订单范围和在制约束以当前会话及工具为准；已开工订单仍原样传入由工具校验，未知号仍原样传入由工具报错。")] string orderId="",
        [Description("目标输入顺序；隔离演示当前十二单为1至12。提前到最前面用1。")] int position=1,
        [Description("用户指定的版本原样传入；未指定留空，使用会话当前版本。")] string version="")
        => _engine.Execute("prepare-reschedule",orderId,position,plan:_context.DemoPlan,version:string.IsNullOrEmpty(version)?_context.DemoPlan?.DemandVersion??InteractiveDemoTools.Version:version,toolName:"prepare_reschedule_demo").ToModelJson();

    [KernelFunction("prepare_mrp_demo")]
    [Description("用户明确要求模拟MRP、算材料需求或按组成分解并抵扣库存/在途时调用，区别于已有齐套状态查询。只准备输入确认卡，不计算。缺订单号留空，不自行选单。")]
    public string PrepareMrpDemo(
        [Description("用户指定的订单号；未指定留空，不替换未知订单。")] string orderId="",
        [Description("用户明确指定的数量才传入，未指定用null；隔离原过程演示只读来源需求量，不接受修改。")] int? quantity=null,
        [Description("未指定供应时必须留空；当前隔离演示读取冻结库存和在途，不支持手选场景。用户明确要求的覆盖由工具拒绝，不能悄悄丢弃。")] string scenario="",
        [Description("用户明确给出的库存kg，原样传入验证，当前隔离演示不允许覆盖。")] decimal? inventory=null,
        [Description("用户明确给出的在途总kg，原样传入验证，当前隔离演示不允许覆盖。")] decimal? inTransit=null)
    {
        if(_context.DemoPlan?.SchemaVersion==4)
        {
            if(quantity.HasValue||inventory.HasValue||inTransit.HasValue||scenario is not ("" or "frozen"))
                return Fail("prepare_mrp_demo","需求、库存和在途由冻结来源读取，不支持手选供应或改写数量，未替换输入。");
            return _engine.Execute("prepare-mrp",orderId,quantity:0,scenario:"frozen",plan:_context.DemoPlan,version:NativeDemoWorkspace.Version,toolName:"prepare_mrp_demo").ToModelJson();
        }
        scenario=string.IsNullOrEmpty(scenario)?_context.DemoPlan?.SupplyScenario??"stock":scenario;
        if (inventory.HasValue && inventory!=0 && inventory!=10000 || inTransit.HasValue && inTransit!=0 && inTransit!=60000 || inventory==0 && inTransit>0)
            return Fail("prepare_mrp_demo","用户供应数量超出演示场景，未替换为固定值。仅支持无供应、库存10000、库存10000加两笔在途共60000（正常或延期）。");
        var expectedStock=scenario=="none"?0m:10000m;
        var expectedTransit=scenario is "supply" or "late"?60000m:0m;
        if (inventory.HasValue && inventory!=expectedStock || inTransit.HasValue && inTransit!=expectedTransit)
            return Fail("prepare_mrp_demo","用户明确给出的库存/在途数量与场景不一致，未改写数量或生成确认卡；请核对输入。");
        var demand=quantity??(_context.DemoPlan!=null&&InteractiveDemoTools.ContainsOrder(orderId)?CoupledDemoEngine.Quantity(_context.DemoPlan,orderId):InteractiveDemoTools.DefaultQuantity(orderId));
        return _engine.Execute("prepare-mrp",orderId,quantity:demand,scenario:scenario,plan:_context.DemoPlan,toolName:"prepare_mrp_demo").ToModelJson();
    }

    [KernelFunction("simulate_reschedule_demo")]
    [Description("仅当本请求系统上下文明示用户已在卡片确认reschedule时调用。没有参数：订单/顺序/版本/基准方案只能取本请求已确认输入，模型不能修改。返回整批确定性模拟试排与摘要，不会采用或写业务系统。")]
    public string SimulateRescheduleDemo() => Confirmed("reschedule","simulate_reschedule_demo");

    [KernelFunction("calculate_mrp_demo")]
    [Description("仅当系统上下文明示本请求用户已在卡片确认mrp时调用。没有参数：从服务端已确认输入取订单、数量、供应场景，不能用模型重填。确定性计算逐层需求/库存/在途/缺口，不扣库存、不改齐套、不下单。")]
    public string CalculateMrpDemo() => Confirmed("mrp","calculate_mrp_demo");

    [KernelFunction("prepare_release_demo")]
    [Description("用户明确要求模拟下达当前版本时调用。检查整批物料和计划完整性，只给确认卡；仍有交期风险也必须展示。不代表真实下发现场。")]
    public string PrepareReleaseDemo() => _engine.Execute("prepare-release",plan:_context.DemoPlan,version:_context.DemoPlan?.DemandVersion??InteractiveDemoTools.Version,toolName:"prepare_release_demo").ToModelJson();

    [KernelFunction("release_demo_plan")]
    [Description("仅当本请求系统明示用户在界面确认release动作时调用。无参数，按已确认且已展示方案执行本地试下发，核对原计划与任务承接，下达后锁定；不写业务系统，不代表实物开工。")]
    public string ReleaseDemoPlan() => Confirmed("release","release_demo_plan");

    private string Confirmed(string action,string tool)
    {
        var input=_context.ConfirmedDemoAction;
        if (input==null || input.Action!=action) return Fail(tool,"本请求未取得对应动作的界面确认，未执行计算；请先使用准备工具并在卡片确认。");
        return _engine.Execute(action,input.OrderId,input.Position,input.Quantity,input.Scenario,_context.DemoPlan,input.Version,tool).ToModelJson();
    }
    private string Fail(string tool,string message)
    {
        _context.ToolCalls.Add(new(){ToolName=tool,Success=false,Summary=message});
        return new ToolResult {Success=false,Summary=message,ErrorMessage=message}.ToModelJson();
    }
}

