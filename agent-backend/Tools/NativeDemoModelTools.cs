using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

// Native workspace exposes only inputs that exist in the audited client flow.
public sealed class NativeDemoModelTools
{
    readonly ToolExecutionContext _context;
    readonly InteractiveDemoTools _engine;
    public NativeDemoModelTools(ToolExecutionContext context){_context=context;_engine=new(context);}

    [KernelFunction("prepare_insert_demo"),Description("准备新增急单插入确认卡。从已审核未交的销售订单来源选择尚未在当前计划中的订单，加入后输入顺序第1位，整批重新试排。不是创建销售订单，也不是把已有订单提前。尚不计算、不采用。")]
    public string PrepareInsertDemo([Description("用户明确指定的新订单号，未指定留空，通过确认卡选择，不自行选单。")]string orderId="")
        =>_engine.Execute("prepare-insert",orderId,1,quantity:0,scenario:"frozen",plan:_context.DemoPlan,version:NativeDemoWorkspace.Version,toolName:"prepare_insert_demo").ToModelJson();

    [KernelFunction("prepare_reschedule_demo"),Description("准备已有计划订单顺序调整确认卡。按用户指定订单与顺序，当前整批试排；已有在制及工序产线约束继续生效。新增急单须用prepare_insert_demo。尚不计算、不采用。")]
    public string PrepareRescheduleDemo([Description("用户明确给出的完整订单号，未指定留空，不自行选单。")]string orderId="",[Description("用户要求的当前批次输入顺序，最前面为1。")]int position=1)
        =>_engine.Execute("prepare-reschedule",orderId,position,quantity:0,scenario:"frozen",plan:_context.DemoPlan,version:NativeDemoWorkspace.Version,toolName:"prepare_reschedule_demo").ToModelJson();

    [KernelFunction("prepare_mrp_demo"),Description("准备当前整批订单物料需求计算确认，包含已采用的新插入订单。目标订单只用于优先查看，未指定留空。需求量、BOM、库存、采购在途直接读取已有业务数据，不通过对话填写或假设数量；核算不自动改变生产计划。")]
    public string PrepareMrpDemo([Description("用户指定的完整订单号；没有指定留空，不自行替换。")]string orderId="")
    {
        if(Regex.IsMatch(_context.InputMessage,@"(?:需求(?:数量)?|库存(?:数量)?|在途(?:数量)?)\s*(?:改为|改成|为|=|：|:)?\s*[+-]?\d|无库存|无在途|供应场景|在途延期"))
        {
            var message="需求、库存与在途由已有业务数据读取，目前不能通过助手改量；本次未计算。";
            _context.ToolCalls.Add(new(){ToolName="prepare_mrp_demo",Success=false,Summary=message});
            return new ToolResult{Success=false,Summary=message,ErrorMessage=message}.ToModelJson();
        }
        return _engine.Execute("prepare-mrp",orderId,quantity:0,scenario:"frozen",plan:_context.DemoPlan,version:NativeDemoWorkspace.Version,toolName:"prepare_mrp_demo").ToModelJson();
    }
    [KernelFunction("simulate_reschedule_demo"),Description("仅当本请求系统明确界面已确认reschedule时调用。无参数，订单与顺序由请求保管。完成本批试排并比较前后变化，尚未采用。")]
    public string SimulateRescheduleDemo()=>Confirmed("reschedule","simulate_reschedule_demo");
    [KernelFunction("simulate_insert_demo"),Description("仅当本请求系统明确界面已确认insert时调用。无参数，从请求读取新订单。原订单提取、分解、确认及整批编排后展示新增单和原有订单影响，尚未采用。")]
    public string SimulateInsertDemo()=>Confirmed("insert","simulate_insert_demo");
    [KernelFunction("calculate_mrp_demo"),Description("仅当本请求系统明确界面已确认mrp时调用。无参数，读取已有需求、库存及在途计算物料需求，不改变生产计划。")]
    public string CalculateMrpDemo()=>Confirmed("mrp","calculate_mrp_demo");
    [KernelFunction("prepare_release_demo"),Description("为当前已采用且已展示的计划准备下发确认卡。须核对范围和日期，由用户在页面确认后执行，尚未下发。")]
    public string PrepareReleaseDemo()=>_engine.Execute("prepare-release",quantity:0,scenario:"frozen",plan:_context.DemoPlan,version:NativeDemoWorkspace.Version,toolName:"prepare_release_demo").ToModelJson();
    [KernelFunction("release_demo_plan"),Description("仅当本请求系统明确界面已确认release时调用。下发当前已展示结果，不重算计划；已有任务保留，不承诺新增派工任务或现场已开工。")]
    public string ReleaseDemoPlan()=>Confirmed("release","release_demo_plan");
    string Confirmed(string action,string tool)
    {
        var input=_context.ConfirmedDemoAction;
        if(input==null||input.Action!=action)
        {
            const string message="本请求缺少对应的界面确认，未执行操作。";
            _context.ToolCalls.Add(new(){ToolName=tool,Success=false,Summary=message});
            return new ToolResult{Success=false,Summary=message,ErrorMessage=message}.ToModelJson();
        }
        return _engine.Execute(action,input.OrderId,input.Position,input.Quantity,input.Scenario,_context.DemoPlan,input.Version,tool).ToModelJson();
    }
}
