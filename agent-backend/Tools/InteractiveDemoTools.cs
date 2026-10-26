using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WePilot.Agent.Tools.Results;
namespace WePilot.Agent.Tools;
public sealed class DemoPlanState
{
    public int SchemaVersion {get;set;}=3;
    public string DemandVersion {get;set;}=InteractiveDemoTools.Version;
    public int Revision {get;set;}
    public string[] OrderIds {get;set;}=InteractiveDemoTools.InitialIds;
    public Dictionary<string,int> DemandQuantities {get;set;}=new();
    public string SupplyScenario {get;set;}="stock";
    public string ReleaseStatus {get;set;}="unreleased";
    public string ReleasedCalculationId {get;set;}="";
    public DemoMrpInput[] MrpInputs {get;set;}=Array.Empty<DemoMrpInput>();
    public string NativeResultId {get;set;}="";
    public string NativeMrpResultId {get;set;}="";
    public string NativeReleaseId {get;set;}="";
}
public sealed class DemoMrpInput
{
    public string OrderId {get;set;}="";public int Quantity {get;set;} public string Scenario {get;set;}="none";
    public int PlanRevision {get;set;} public string[] OrderIds {get;set;}=Array.Empty<string>();
}
public sealed class InteractiveDemoTools
{
    public const string Version="261015001",ReleasedVersion="261015002",AsOf="2026-10-15T09:00:00+08:00";
    private readonly ToolExecutionContext _context;
    private static readonly JsonElement Snapshot=Load();
    public static string[] InitialIds=>CoupledDemoEngine.LockedIds.Concat(Snapshot.GetProperty("orders").EnumerateArray().Where(o=>o.GetProperty("productionStatus").GetString()=="notStarted").Select(o=>o.GetProperty("id").GetString()!)).ToArray();
    public static string[] PendingIds=>InitialIds.Where(id=>CoupledDemoEngine.LockedRemaining(id)==0).ToArray();
    public InteractiveDemoTools(ToolExecutionContext context)=>_context=context;
    internal static bool ValidIds(string[]? ids)=>ids!=null&&ids.Length==InitialIds.Length&&ids.Distinct().Count()==InitialIds.Length&&ids.All(InitialIds.Contains)&&ids.Take(CoupledDemoEngine.LockedIds.Length).SequenceEqual(CoupledDemoEngine.LockedIds);
    public static bool ContainsOrder(string id)=>InitialIds.Contains((id??"").Trim().ToUpperInvariant());
    public static bool ValidPlan(DemoPlanState p)=>p.SchemaVersion==4?NativeDemoWorkspace.Valid(p):p.SchemaVersion==3&&p.DemandVersion==Version&&p.Revision>=0&&p.Revision<int.MaxValue-1&&ValidIds(p.OrderIds)&&p.MrpInputs!=null&&p.MrpInputs.Length==0&&p.DemandQuantities!=null&&p.DemandQuantities.All(kv=>PendingIds.Contains(kv.Key)&&kv.Value is >=1 and <=100000)&&(p.SupplyScenario is "none" or "stock" or "supply" or "late")&&(p.ReleaseStatus is "unreleased" or "released")&&(p.ReleaseStatus!="released"||p.ReleasedCalculationId==CoupledDemoEngine.CalculationId(p));
    private static bool IsOperation(string m)
    {
        var r=Regex.Replace(m,"计算结果|分解结果|运算结果","结果");
        if(Regex.IsMatch(r,"MRP",RegexOptions.IgnoreCase)&&Regex.IsMatch(r,"查询|查看|已保存")&&!Regex.IsMatch(r,"计算|运算|重算|分解|执行|插单|插队|重排|试排|调序|下发|下达"))return false;
        return Regex.IsMatch(m,"插单|插队|提前安排|重排|试排|调序|优先级|调整顺序|MRP|需求分解|计算物料|物料需求计算|计算演示|模拟下达|模拟下发",RegexOptions.IgnoreCase);
    }
    public static ToolResult? Guard(string m)
    {
        if(IsOperation(m)&&Regex.IsMatch(m,"真实|正式|实时|原系统|业务系统|业务库"))return Failure("真实插单、重排、MRP和下达尚未接入；未执行业务操作，未用模拟结果替代。");
        if(IsOperation(m)&&!Regex.IsMatch(m,"演示|模拟"))return Failure("当前仅支持本地模拟操作，请明确选择急单重排演示、MRP计算演示或模拟下达。");
        return null;
    }
    public ToolResult? Route(string m,DemoPlanState? plan)
    {
        var guard=Guard(m);if(guard!=null)return guard;if(!IsOperation(m))return null;
        if(Regex.IsMatch(m,"模拟下达|模拟下发"))return Execute("prepare-release",plan:plan);
        var ids=Regex.Matches(m,@"(?:Z[0-9]{7,9}|XS[0-9]+)\b",RegexOptions.IgnoreCase).Select(x=>x.Value.ToUpperInvariant()).Distinct().ToArray();
        if(ids.Length>1)return Failure("每次请选择一个目标订单，计算会重算整批12单。");
        var id=ids.FirstOrDefault()??"";var demandMessage=Regex.Replace(m,@"(?:库存|在途)(?:数量)?\s*(?:为|=|：|:)?\s*[+-]?\d+(?:\.\d+)?","");
        var q=Regex.Match(demandMessage,@"(?:成品需求(?:数量)?|需求(?:数量)?|数量)\s*(?:改为|改成|为|=|：|:)?\s*([+-]?\d+(?:\.\d+)?)");var quantity=plan!=null&&ValidPlan(plan)&&ContainsOrder(id)?CoupledDemoEngine.Quantity(plan,id):DefaultQuantity(id);
        if(q.Success&&!int.TryParse(q.Groups[1].Value,out quantity))return Failure("需求数量须为1至100000整数件。");
        var mrp=Regex.IsMatch(m,"MRP|需求分解|计算物料|物料需求计算",RegexOptions.IgnoreCase);var pos=Regex.Match(m,@"第\s*(\d+)\s*位");
        var stockInput=Regex.Match(m,@"库存(?:数量)?\s*(?:为|=|：|:)?\s*([+-]?\d+(?:\.\d+)?)");var transitInput=Regex.Match(m,@"在途(?:数量)?\s*(?:为|=|：|:)?\s*([+-]?\d+(?:\.\d+)?)");
        bool Allowed(Match match,decimal value)=>!match.Success||decimal.TryParse(match.Groups[1].Value,NumberStyles.Number,CultureInfo.InvariantCulture,out var n)&&(n==0||n==value);
        if(!Allowed(stockInput,10000)||!Allowed(transitInput,60000))return Failure("供应数量超出演示场景，未替换为固定值；库存仅0或10000kg，新增在途总量仅0或60000kg。");
        var noStock=Regex.IsMatch(m,"无库存|没有库存")||stockInput.Success&&stockInput.Groups[1].Value=="0";
        var noTransit=Regex.IsMatch(m,"无在途|没有在途")||transitInput.Success&&transitInput.Groups[1].Value=="0";
        var hasTransit=!noTransit&&(transitInput.Success&&decimal.Parse(transitInput.Groups[1].Value,CultureInfo.InvariantCulture)>0||Regex.IsMatch(m,"有在途|在途抵扣|确认到货"));
        if(noStock&&hasTransit)return Failure("无库存且有在途不属于当前四种模拟供应场景，未替换输入。");
        var scenario=noStock?"none":Regex.IsMatch(m,"延期|迟到|晚到")?"late":hasTransit?"supply":noTransit||Regex.IsMatch(m,"有库存|库存抵扣")?"stock":plan?.SupplyScenario??"stock";
        return Execute(mrp?"prepare-mrp":"prepare-reschedule",id,pos.Success?int.Parse(pos.Groups[1].Value):1,quantity,scenario,plan,m.Contains("已下达")?ReleasedVersion:Version);
    }
    public ToolResult Execute(string action,string orderId="",int position=1,int quantity=12500,string scenario="stock",DemoPlanState? plan=null,string version=Version,string? toolName=null)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();var result=Compute(action,(orderId??"").Trim().ToUpperInvariant(),position,quantity,scenario,plan??new(),version);
        _context.ToolCalls.Add(new(){ToolName=toolName??$"demo_{action}",Arguments=new{orderId,position,quantity,scenario,version,revision=plan?.Revision??0},Success=result.Success,Summary=result.Summary,ElapsedMs=(int)watch.ElapsedMilliseconds});_context.UiPayload.AddRange(result.UiPayload);return result;
    }
    private ToolResult Compute(string action,string id,int position,int quantity,string scenario,DemoPlanState plan,string version)
    {
        if(_context.RealSourceRequested)return Failure("真实业务操作尚未接入，阻止模拟替代真实来源。");
        if(plan.SchemaVersion==4)return NativeDemoWorkspace.Execute(action,id,position,quantity,scenario,plan,version);
        if(!ValidPlan(plan))return Failure("模拟状态无效或属于旧版独立MRP演示，请新建对话进入完整联动，旧结果保留。");
        if(action is not ("reschedule" or "mrp" or "current" or "release" or "prepare-reschedule" or "prepare-mrp" or "prepare-release"))return Failure("未知模拟操作。");
        if(version=="SIM-20261015")version=Version;
        if(version is ReleasedVersion or "SIM-RELEASED")return Failure($"需求版本{ReleasedVersion}（模拟）已下达，拒绝调序重算。");
        if(version!=Version)return Failure("版本不存在或下达状态未知，拒绝操作。");
        if(plan.ReleaseStatus=="released"&&action!="current")return Failure("当前模拟版本已下达并锁定，不能重复下达、修改需求或重排；请新建模拟对话。");
        if(id.Length>0&&!ContainsOrder(id))return Failure($"订单{id}不在本版12单范围，未替换为其他订单。");
        if(action.Contains("mrp")&&(quantity is <1 or >100000))return Failure("演示需求数量须为1至100000整数件。");
        if(action.Contains("mrp")&&scenario is not ("none" or "stock" or "supply" or "late"))return Failure("未知库存/在途场景，未使用默认值替代。");
        if(id.Length>0&&CoupledDemoEngine.LockedRemaining(id)>0&&(action.Contains("reschedule")||action.Contains("mrp")&&quantity!=CoupledDemoEngine.LockedRemaining(id)))return Failure("该订单已开工，剩余模拟需求、物料和作业已锁定，不能调序或改量；可以查询，供应场景更新仅影响未锁定需求。");
        if(action.Contains("reschedule")&&(position<1||position>PendingIds.Length))return Failure("待排队列顺序须为第1至4位。");
        var before=CoupledDemoEngine.Run(plan);var kind=action.Replace("prepare-","");
        if(kind=="release"&&(before.Simulation!.Unscheduled+before.Simulation.PartiallyScheduled>0||before.Simulation.PurchaseNeed>0))return Failure("当前仍有缺料或未排完整订单，不能模拟下达；请先补足模拟供应并核对完整计划。");
        if(action.StartsWith("prepare-")||(id.Length==0&&kind is "mrp" or "reschedule"))
        {
            var text=kind=="release"?$"准备模拟下达：12单，新增4张未开工订单的12条工序任务，8张已开工的锁定作业继续保留，仍有{before.Simulation!.RiskCount}单交期风险，请核对后确认。":$"准备{(kind=="mrp"?"整批MRP及物料约束排程":"整批调序试排")}，目标{(id.Length>0?id:"尚未选择")}；确认只试算，采用后更新整个模拟版本。";
            return Result(kind,text,new{view="setup",action=kind,source="synthetic-demo",sourceLabel="本地模拟数据 · 非实时业务",version,demandVersion=Version,asOf=AsOf,releaseStatus=plan.ReleaseStatus,orders=before.Orders,baseRevision=plan.Revision,baseState=plan,baseOrderIds=plan.OrderIds,orderId=id,position,quantity,scenario,calculationId=before.CalculationId,workspace=DemoWorkspaceProjection.Describe(plan,id),rules=Rules});
        }
        var proposed=CoupledDemoEngine.Copy(plan);if(kind!="current")proposed.Revision++;
        if(kind=="reschedule"){var ids=plan.OrderIds.Where(x=>x!=id).ToList();ids.Insert(CoupledDemoEngine.LockedIds.Length+position-1,id);proposed.OrderIds=ids.ToArray();}
        if(kind=="mrp"){if(PendingIds.Contains(id))proposed.DemandQuantities[id]=quantity;proposed.SupplyScenario=scenario;}
        if(kind=="release"){proposed.ReleaseStatus="released";proposed.ReleasedCalculationId=before.CalculationId;}
        var after=CoupledDemoEngine.Run(proposed);
        var changes=after.Orders.Select(a=>{var b=before.Orders.Single(o=>o.OrderId==a.OrderId);return new{orderId=a.OrderId,product=a.Product,quantity=a.Quantity,orderQuantity=a.OrderQuantity,dueDate=a.DueDate,beforePosition=b.QueuePosition,afterPosition=a.QueuePosition,isLocked=a.IsLocked,beforeQuantity=b.Quantity,afterQuantity=a.Quantity,beforeStart=b.PlanStart,afterStart=a.PlanStart,beforeEnd=b.PlanEnd,afterEnd=a.PlanEnd,beforeRisk=b.Risk,afterRisk=a.Risk,beforeStatus=b.ScheduleStatus,afterStatus=a.ScheduleStatus,beforeMaterial=b.Material,afterMaterial=a.Material,reason=a.Reason,days=b.PlanEnd.Length>0&&a.PlanEnd.Length>0?(int?)(DateOnly.Parse(a.PlanEnd).DayNumber-DateOnly.Parse(b.PlanEnd).DayNumber):null};}).ToArray();
        var target=after.Orders.FirstOrDefault(o=>o.OrderId==id);var mat=target?.Material;
        var summary=kind=="release"?$"模拟下达确认完成：12单同版，新增12条待执行工序任务，保留24条已开工锁定模拟任务，来源计算{after.CalculationId}；仅本地会话，未向现场下发或确认实物开工。":$"{(kind=="mrp"?"整批MRP及排程":kind=="current"?"当前模拟方案":"整批调序")}：{id}，整批{after.Simulation!.FullyScheduled}单排完整、{after.Simulation.Unscheduled}单未排、{after.Simulation.RiskCount}单风险，钢材仍需补料{after.Simulation.PurchaseNeed:0.00}kg。"+(kind=="current"?"":"尚未采用。");
        if(target!=null&&mat!=null&&kind is "mrp" or "reschedule")
        {
            var old=before.Orders.Single(o=>o.OrderId==id);
            summary+=$"本单需求{target.Quantity}件（原订单{target.OrderQuantity}件），按0.55089kg/件毛需求{mat.GrossNeed:0.00}kg；库存分配{old.Material!.StockUsed:0.00}→{mat.StockUsed:0.00}kg，在途分配{old.Material.TransitUsed:0.00}→{mat.TransitUsed:0.00}kg，物料就绪{(mat.ReadyDate.Length>0?mat.ReadyDate:"尚无补足日期")}，计划结束{(old.PlanEnd.Length>0?old.PlanEnd:"未排完整")}→{(target.PlanEnd.Length>0?target.PlanEnd:"未排完整")}，交期{target.DueDate}。";
            summary+="其他未开工订单："+string.Join("；",changes.Where(c=>!c.isLocked&&c.orderId!=id).Select(c=>$"{c.orderId}结束{(c.beforeEnd.Length>0?c.beforeEnd:"未排完整")}→{(c.afterEnd.Length>0?c.afterEnd:"未排完整")}"))+"。8张已开工订单的锁定物料和作业保持不变；未来到货先等到货再排。";
        }
        return Result(kind,summary,new{view=kind,source="synthetic-demo",sourceLabel="共享物料与产能模拟 · 非实时业务",version,demandVersion=Version,dataVersion=CoupledDemoEngine.DataVersion,asOf=AsOf,releaseStatus=proposed.ReleaseStatus,baseRevision=plan.Revision,baseState=plan,baseOrderIds=plan.OrderIds,proposedPlan=proposed,orderId=id,quantity,scenario,orderQuantity=target?.OrderQuantity,workspace=DemoWorkspaceProjection.Describe(proposed,id),calculationId=after.CalculationId,before,after,simulation=after.Simulation,changes,headline=summary,beforeChart=Chart(before),afterChart=Chart(after),grossNeed=mat?.GrossNeed,stockUsed=mat?.StockUsed,transitUsed=mat?.TransitUsed,purchaseNeed=mat?.PurchaseNeed,stock=after.Simulation?.Stock,transit=after.Simulation?.Transit,needDate=mat?.ReadyDate,layers=Layers(target),formula=mat==null?"":$"{target!.Quantity} × 0.55089 = {mat.GrossNeed:0.00}kg；{mat.GrossNeed:0.00} − 库存{mat.StockUsed:0.00} − 在途{mat.TransitUsed:0.00} = 补料{mat.PurchaseNeed:0.00}kg。抵扣来自整批共享分配。",rules=Rules});
    }
    private static object[] Layers(DemoOrder? target)=>target==null?Array.Empty<object>():new object[]{new{level=0,material="SIM-FG",name=target.Product,unit="件",rate=1m,demand=(decimal)target.Quantity,kind="成品需求"},new{level=1,material="SIM-HALF",name="模拟半成品",unit="件",rate=1m,demand=(decimal)target.Quantity,kind="生产需求"},new{level=2,material="SIM-BLANK",name="模拟毛坯",unit="件",rate=1m,demand=(decimal)target.Quantity,kind="生产需求"},new{level=3,material="SIM-STEEL",name="模拟钢材",unit="kg",rate=CoupledDemoEngine.Rate,demand=target.Material!.GrossNeed,kind="末级材料"}};
    public static readonly string[] Rules={"BOM、库存、供应、日历、产能和任务均为本地模拟，originalApiInvoked=false，不是原订单真实产品结构。","12单共用SIM-STEEL，已开工8单优先保留锁定库存和作业，不可调序或修改剩余量：每件0.55089kg；成品→半成品→毛坯均1:1，中间层无库存，损耗0、安全库存0；钢材四舍五入两位。先库存后按到货日期的在途，依订单顺序分配，每批仅抵扣一次。","库存10000kg；供应情境新增在途20000kg于10-04、40000kg于10-08；延期情境第二批改10-12；无供应情境仅撤去未锁定供应，已开工8单的锁定库存保留。缺料且无补足日期的订单不排，有在途的订单最早在所需物料全部到货日排入。","窗口2026-10-01至10-31，每天白晚两班、周末工作；冷镦/热处理/磨加工各一专用线，班产能12500/6250/12500件；整单前工序完成后下一班进入后工序，同线同班不重叠。不考虑换型、替代料或原引擎其他约束。","需求修改更新本版本成品需求，不改原订单总量；采用同时重算整批物料及排程。排完整不等于完工，在途抵扣不代表今天可开工。","模拟下达须明确确认且整批排完整、无物料缺口，仍可能有交期风险；任务由当前计划记录派生，下达后锁定本地版本，不代表现场下发或实物开工。"};
    internal static DemoOrder Order(string id){var o=Snapshot.GetProperty("orders").EnumerateArray().Single(x=>x.GetProperty("id").GetString()==id);return new(id,o.GetProperty("product").GetString()!,o.GetProperty("quantity").GetInt32(),o.GetProperty("dueDate").GetString()!,0,"","",false){OrderQuantity=o.GetProperty("quantity").GetInt32()};}
    internal static ScheduleResult Schedule(string[] ids)=>CoupledDemoEngine.Run(new(){OrderIds=ids});
    internal static object Chart(ScheduleResult plan)
    {
        var max=plan.Tasks.Length==0?2:plan.Tasks.Max(t=>t.Slot)+1;if(max%2!=0)max++;
        var columns=Enumerable.Range(0,max).Select(i=>new{key=$"{CoupledDemoEngine.Date(i)}-{i%2}",date=CoupledDemoEngine.Date(i),weekday=CultureInfo.GetCultureInfo("zh-CN").DateTimeFormat.GetDayName(CoupledDemoEngine.Start.AddDays(i/2).DayOfWeek),shift=i%2==0?"白班":"晚班"}).ToArray();
        var rows=plan.Orders.SelectMany(o=>Enumerable.Range(0,3).Where(s=>plan.Tasks.Any(t=>t.OrderId==o.OrderId&&t.StageIndex==s)).Select(s=>new{key=$"{o.OrderId}|{s}",orderId=o.OrderId,lineName=$"模拟线{s+1}",productName=o.Product,productSpec="模拟工艺",stage=CoupledDemoEngine.Stages[s],planQuantity=o.Quantity,planStart=plan.Tasks.Where(t=>t.OrderId==o.OrderId&&t.StageIndex==s).Min(t=>t.Date),planEnd=plan.Tasks.Where(t=>t.OrderId==o.OrderId&&t.StageIndex==s).Max(t=>t.Date)})).ToArray();
        var items=plan.Tasks.Select(t=>new{id=t.PlanRecordId,rowIndex=Array.FindIndex(rows,r=>r.key==$"{t.OrderId}|{t.StageIndex}"),columnIndex=t.Slot,orderId=t.OrderId,stage=t.Stage,operation=t.Stage,date=t.Date,shift=t.Shift,lineId=$"SIM-L{t.StageIndex+1}",quantity=t.Quantity,plannedHours=8m*t.Quantity/t.Capacity,tone=t.Shift=="白班"?"day":"night"}).ToArray();
        return new{kind="gantt",title="共享物料约束的整批模拟排程",columns,rows,items,emptyText="无可排计划，请核对物料缺口及供应日期"};
    }
    private static ToolResult Result(string action,string summary,object spec)=>new(){Success=true,Summary=summary,AgentContext=new{source="synthetic-demo",demandVersion=Version,action,summary,asOf=AsOf,originalApiInvoked=false},UiPayload=new(){new(){Type="interactive-demo",Title=action=="mrp"?"MRP 计算演示":action=="release"?"模拟下达确认":"急单重排演示",Spec=spec}}};
    private static ToolResult Failure(string message)=>new(){Success=false,Summary=message,ErrorMessage=message};
    private static JsonElement Load(){using var stream=typeof(InteractiveDemoTools).Assembly.GetManifestResourceStream("WePilot.Agent.Data.production-demo.json")!;using var doc=JsonDocument.Parse(stream);return doc.RootElement.Clone();}
    public static int DefaultQuantity(string id){id=(id??"").Trim().ToUpperInvariant();return ContainsOrder(id)?(CoupledDemoEngine.LockedRemaining(id)>0?CoupledDemoEngine.LockedRemaining(id):Order(id).Quantity):12500;}
    public record DemoOrder(string OrderId,string Product,int Quantity,string DueDate,int Position,string PlanStart,string PlanEnd,bool Risk){public int OrderQuantity{get;init;}public bool IsLocked{get;init;}public int QueuePosition{get;init;}public string ProductionStatus{get;init;}="";public string ScheduleStatus{get;init;}="unscheduled";public string Reason{get;init;}="";public CoupledDemoEngine.MaterialAllocation? Material{get;init;}}
    public record DemoTask(string OrderId,int StageIndex,string Stage,int Slot,int Quantity,int Capacity,string Date,string Shift){public string PlanRecordId{get;init;}="";}
    public record ScheduleResult(DemoOrder[] Orders,DemoTask[] Tasks){public string CalculationId{get;init;}="";public CoupledDemoEngine.Simulation? Simulation{get;init;}public CoupledDemoEngine.Lot[] SupplyLots{get;init;}=Array.Empty<CoupledDemoEngine.Lot>();}
}

