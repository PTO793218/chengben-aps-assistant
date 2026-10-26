using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WePilot.Agent.Tools.Results;
namespace WePilot.Agent.Tools;
public static class DemoWorkspaceProjection
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static bool Applies(DemoPlanState? plan,string orderId="")=>plan!=null&&(plan.SchemaVersion==4||string.IsNullOrWhiteSpace(orderId)||InteractiveDemoTools.ContainsOrder(orderId));
    public static object Describe(DemoPlanState plan,string orderId="")
    {
        if(plan.SchemaVersion==4)return NativeDemoWorkspace.Describe(plan,orderId);
        var run=CoupledDemoEngine.Run(plan);
        return new{demandVersion=InteractiveDemoTools.Version,source="synthetic-demo",dataVersion=CoupledDemoEngine.DataVersion,calculationId=run.CalculationId,releaseStatus=plan.ReleaseStatus,planLabel="同一需求、供应与产能方案",supplyScenario=plan.SupplyScenario,simulation=run.Simulation,supplyLots=run.SupplyLots,orders=run.Orders.Where(o=>orderId.Length==0||o.OrderId==orderId).ToArray()};
    }
    private static ToolResult? Check(DemoPlanState p,string version="")
    {
        if(!InteractiveDemoTools.ValidPlan(p))return Fail("当前模拟方案无效或属于旧版，请新建对话进入完整联动，未回退混用历史结果。");
        if(!string.IsNullOrEmpty(version)&&version!=InteractiveDemoTools.Version)return Fail("所请求需求版本与当前模拟版本不同，未混用历史数据。");
        return null;
    }
    public static ToolResult Schedule(DemoPlanState p,string view,string orderId,string startDate,string endDate,string version,string scope)
    {
        if(p.SchemaVersion==4)return NativeDemoWorkspace.Query(p,view,orderId,version,startDate,endDate);
        var invalid=Check(p,version);if(invalid!=null)return invalid;
        var all=CoupledDemoEngine.Run(p);
        if(!DateOnly.TryParseExact(startDate.Length==0?"2026-10-01":startDate,"yyyy-MM-dd",out var start)||!DateOnly.TryParseExact(endDate.Length==0?"2026-10-31":endDate,"yyyy-MM-dd",out var end)||start>end||end.DayNumber-start.DayNumber>62)return Fail("日期使用YYYY-MM-DD，开始不晚于结束，范围最多63天。");
        var orders=all.Orders.Where(o=>(orderId.Length==0||o.OrderId==orderId)&&(view!="risk"||o.Risk)&&(scope!="incomplete"||o.ScheduleStatus!="fullyScheduled")).ToArray();
        var ids=orders.Select(o=>o.OrderId).ToHashSet();var tasks=all.Tasks.Where(t=>ids.Contains(t.OrderId)&&DateOnly.Parse(t.Date)>=start&&DateOnly.Parse(t.Date)<=end).ToArray();
        var chart=JsonSerializer.SerializeToNode(InteractiveDemoTools.Chart(new(orders,tasks)),Json)!.AsObject();
        var columns=new JsonArray();for(var day=start;day<=end;day=day.AddDays(1))foreach(var shift in new[]{"白班","晚班"})columns.Add(JsonSerializer.SerializeToNode(new{key=$"{day:yyyy-MM-dd}-{shift}",date=day.ToString("yyyy-MM-dd"),weekday=CultureInfo.GetCultureInfo("zh-CN").DateTimeFormat.GetDayName(day.DayOfWeek),shift},Json));
        chart["columns"]=columns;foreach(var item in chart["items"]!.AsArray())item!["columnIndex"]=(DateOnly.Parse(item["date"]!.GetValue<string>()).DayNumber-start.DayNumber)*2+(item["shift"]!.GetValue<string>()=="晚班"?1:0);
        var summary=$"需求版本{InteractiveDemoTools.Version}（模拟，{(p.ReleaseStatus=="released"?"已模拟下达":"未下达")}），当前范围{orders.Length}单，{orders.Count(o=>o.ScheduleStatus=="fullyScheduled")}单排完整，{orders.Count(o=>o.ScheduleStatus!="fullyScheduled")}单未排完整，{orders.Count(o=>o.Risk)}单交期风险。"+string.Join("；",orders.Select(o=>$"{o.OrderId}：{o.Reason}"))+"。物料分配和到货日期已约束本计划，不代表完工或现场开工。";
        if(view=="order"&&orders.Length==1)summary+=$"订单{orders[0].OrderId}：本版需求{orders[0].Quantity}件（原订单{orders[0].OrderQuantity}件），计划开始{Show(orders[0].PlanStart)}，计划结束{Show(orders[0].PlanEnd)}，交期{orders[0].DueDate}。";
        return new(){Success=true,Summary=summary,AgentContext=new{source="synthetic-demo",demandVersion=InteractiveDemoTools.Version,calculationId=all.CalculationId,summary,asOf=InteractiveDemoTools.AsOf},UiPayload=new(){new(){Type="aps-analysis",Title=view=="order"?$"订单{orderId}排程":view=="risk"?"计划交期风险":"生产计划总览",Spec=new{viewType=view,scheduleScope=scope,source="synthetic-demo",sourceLabel="共享物料约束的模拟计划 · 非实时业务",eyebrow="APS PLAN QUERY",demandVersion=InteractiveDemoTools.Version,snapshotId=all.CalculationId,calculationId=all.CalculationId,simulation=all.Simulation,asOf=InteractiveDemoTools.AsOf,range=new{start=start.ToString("yyyy-MM-dd"),end=end.ToString("yyyy-MM-dd")},headline=summary,metrics=new[]{new{label="当前范围订单",value=orders.Length,unit="单"},new{label="已排完整",value=orders.Count(o=>o.ScheduleStatus=="fullyScheduled"),unit="单"},new{label="未排完整",value=orders.Count(o=>o.ScheduleStatus!="fullyScheduled"),unit="单"},new{label="交期风险",value=orders.Count(o=>o.Risk),unit="单"}},chart,detail=new{title="同一计算的需求与排程",columns=new[]{new{key="orderId",label="订单"},new{key="quantity",label="本版需求"},new{key="orderQuantity",label="原订单量"},new{key="planStart",label="计划开始"},new{key="plannedEnd",label="计划结束"},new{key="dueDate",label="交期"},new{key="reason",label="结果依据"}},rows=orders.Select(o=>new{o.OrderId,o.Quantity,o.OrderQuantity,planStart=Show(o.PlanStart),plannedEnd=Show(o.PlanEnd),o.DueDate,o.ScheduleStatus,o.Reason,risk=o.Risk?"有":"无"}).ToArray()},workspace=Describe(p,orderId),limitations=InteractiveDemoTools.Rules}}}};
    }
    public static ToolResult Material(DemoPlanState p,string scope,string orderId,string query,string version)
    {
        if(p.SchemaVersion==4)
        {
            if(scope is not ("all" or "order" or "material"))return Fail("物料范围须为all、order或material。");
            if(scope=="order"&&orderId.Length==0)return Fail("按订单查询需要订单号。");
            if(scope=="material"&&string.IsNullOrWhiteSpace(query))return Fail("按物料查询需要名称或物料号。");
            return NativeDemoWorkspace.Query(p,"material",orderId,version,filter:query.Trim());
        }
        var invalid=Check(p,version);if(invalid!=null)return invalid;var run=CoupledDemoEngine.Run(p);var orders=run.Orders;
        var snapshot=JsonSerializer.SerializeToElement(new{demandVer=InteractiveDemoTools.Version,snapshotId=run.CalculationId,source="synthetic-demo",systemNo="SIM",asOf=InteractiveDemoTools.AsOf,
            orders=orders.Select(o=>new{orderId=o.OrderId,productNo=$"SIM-{o.OrderId}",productName=o.Product,orderQuantity=o.Quantity,dueDate=o.DueDate}),
            orderStatuses=orders.Select(o=>new{orderId=o.OrderId,status=o.Material!.PurchaseNeed==0?"full_tag_t":"full_tag_f",riskLabel=o.Material.Status=="shortage"?"模拟缺料":o.Material.Status=="waiting-inbound"?"等待模拟到货":"模拟库存已覆盖",riskDetail=o.Reason}),
            productBomMaterials=orders.Select(o=>new{productNo=$"SIM-{o.OrderId}",materialNo="SIM-STEEL",materialName="模拟共享钢材",bomLevel="004",unit="kg"}),
            readinessResults=orders.Select(o=>new{orderId=o.OrderId,materialNo="SIM-STEEL",fullTag=o.Material!.PurchaseNeed==0?"T":"F",materialNum=o.Material.GrossNeed,remainNum=o.Material.StockUsed,thisRemainNum=o.Material.TransitUsed,totalRemainNum=o.Material.PurchaseNeed,supplyOrderNo=string.Join(" / ",o.Material.Allocations.Select(a=>a.SupplyOrderNo)),supplyDeliveryDate=o.Material.ReadyDate}),
            materialImpactSummary=new[]{new{materialNo="SIM-STEEL",orderCount=run.Orders.Length,orderIds=p.OrderIds,bomRowCount=run.Orders.Length}},limitations=InteractiveDemoTools.Rules},Json);
        var result=BusinessMaterialReadiness.Build(snapshot,scope,orderId,query,version);if(!result.Success)return result;
        var selected=orders.Where(o=>orderId.Length==0||o.OrderId==orderId);
        result.Summary=$"当前模拟版本共享钢材毛需求{run.Simulation!.GrossNeed:0.00}kg，库存分配{run.Simulation.StockUsed:0.00}kg，在途分配{run.Simulation.TransitUsed:0.00}kg，仍需补料{run.Simulation.PurchaseNeed:0.00}kg。"+string.Join("；",selected.Select(o=>$"{o.OrderId}需求{o.Quantity}件：毛需求{o.Material!.GrossNeed:0.00}−库存{o.Material.StockUsed:0.00}−在途{o.Material.TransitUsed:0.00}=补料{o.Material.PurchaseNeed:0.00}kg，物料就绪{Show(o.Material.ReadyDate)}"))+"。模拟覆盖标记由本地分配计算，不是原APS齐套记录；未来到货不代表今天可开工。";
        Attach(result,p,orderId,run);
        foreach(var block in result.UiPayload)
        {
            var spec=(JsonObject)block.Spec!;spec["sourceLabel"]="本地共享物料计算 · 非原APS齐套记录";
            var metrics=spec["metrics"]!.AsArray();metrics[1]!["label"]="模拟供应已覆盖";metrics[2]!["label"]="模拟物料缺口";metrics[3]!["label"]="待核对";
            ReplaceLabels(spec);
        }
        return result;
    }
    private static void ReplaceLabels(JsonNode? node)
    {
        if(node is JsonObject obj)foreach(var kv in obj.ToArray()){if(kv.Value is JsonValue v&&v.TryGetValue<string>(out var text)&&text!=null){if(text=="已有齐套标记")obj[kv.Key]="模拟供应已覆盖";else if(text=="未获得齐套标记")obj[kv.Key]="模拟物料缺口";else if(text=="齐套")obj[kv.Key]="模拟已覆盖";}else ReplaceLabels(kv.Value);}
        else if(node is JsonArray arr)foreach(var child in arr)ReplaceLabels(child);
    }
    public static ToolResult Dispatch(DemoPlanState p,string lineId,string orderId)
    {
        if(p.SchemaVersion==4)return NativeDemoWorkspace.Query(p,"dispatch",orderId,filter:lineId);
        var invalid=Check(p);if(invalid!=null)return invalid;var run=CoupledDemoEngine.Run(p);var stage=Array.IndexOf(new[]{"SIM-L1","SIM-L2","SIM-L3"},lineId);
        if(lineId.Length>0&&stage<0)return Fail("当前模拟方案仅支持SIM-L1、SIM-L2、SIM-L3。");
        var orders=run.Orders.Where(o=>orderId.Length==0||o.OrderId==orderId).ToArray();
        var lines=lineId.Length==0?"SIM-L1 / SIM-L2 / SIM-L3":lineId;
        var groups=run.Tasks.Where(t=>(orderId.Length==0||t.OrderId==orderId)&&(lineId.Length==0||t.StageIndex==stage)).GroupBy(t=>new{t.OrderId,t.StageIndex}).ToArray();
        var tasks=groups.Where(g=>CoupledDemoEngine.LockedRemaining(g.Key.OrderId)>0||p.ReleaseStatus=="released").Select(g=>new{taskGuid=$"{(CoupledDemoEngine.LockedRemaining(g.Key.OrderId)>0?CoupledDemoEngine.LockedCalculationId:run.CalculationId)}-{g.Key.OrderId}-S{g.Key.StageIndex+1}",taskNo=$"SIM-TASK-{g.Key.OrderId}-S{g.Key.StageIndex+1}",pdPlanGuid=$"{run.CalculationId}-{g.Key.OrderId}",productName=orders.Single(o=>o.OrderId==g.Key.OrderId).Product,lineId=$"SIM-L{g.Key.StageIndex+1}",taskStart=g.Min(t=>t.Date),taskEnd=g.Max(t=>t.Date),taskQuantity=g.Sum(t=>t.Quantity),deliveryQuantity=0,status=CoupledDemoEngine.LockedRemaining(g.Key.OrderId)>0?"run":"created",locked=CoupledDemoEngine.LockedRemaining(g.Key.OrderId)>0,sourceCalculationId=CoupledDemoEngine.LockedRemaining(g.Key.OrderId)>0?CoupledDemoEngine.LockedCalculationId:run.CalculationId,currentCalculationId=run.CalculationId,sourcePlanRecordIds=g.Select(t=>t.PlanRecordId).ToArray()}).ToArray();
        var snapshot=JsonSerializer.SerializeToElement(new{snapshotId=run.CalculationId,source="synthetic-demo",sourceLabel="本地模拟计划及派生任务",systemNo="SIM",asOf=InteractiveDemoTools.AsOf,orders=orders.Select(o=>new{orderNo=o.OrderId,pdPlanGuid=$"{run.CalculationId}-{o.OrderId}",productName=o.Product,quantity=o.Quantity,lineId=lines,planStart=o.PlanStart,planEnd=o.PlanEnd}),plans=orders.Where(o=>o.ScheduleStatus=="fullyScheduled").Select(o=>new{planGuid=$"{run.CalculationId}-{o.OrderId}",planNo=o.OrderId,productName=o.Product,planQuantity=o.Quantity,lineId=lines,planStart=o.PlanStart,planEnd=o.PlanEnd}),dispatchTasks=tasks,latestTasks=Array.Empty<object>()},Json);
        var result=BusinessOrderDispatch.Build(snapshot,lineId,orderId);if(!result.Success)return result;
        result.Summary=$"当前版本{(p.ReleaseStatus=="released"?"已模拟下达":"待排需求尚未下达")}，当前范围{orders.Length}单，保留{tasks.Count(t=>t.locked)}条已开工锁定模拟任务，新增{tasks.Count(t=>!t.locked)}条待执行模拟任务；新任务来自当前计算，锁定任务及计划来源保持不变。没有本轮真实报工或现场下发。";
        Attach(result,p,orderId,run);
        var root=(JsonObject)result.UiPayload.Single().Spec!;
        root["releaseStatusNote"]=p.ReleaseStatus=="released"?"本版待排需求已模拟下达 · 锁定作业保留 · 未向现场下发":"本版待排需求未下达，8单既有锁定模拟任务保留";
        root["risk"]=JsonSerializer.SerializeToNode(new{headline=run.Simulation!.RiskCount>0?$"仍有{run.Simulation.RiskCount}单计划交期风险，请继续查看同版本风险。":"当前模拟计划无交期风险。",items=orders.Where(o=>o.Risk).Select(o=>new{orderNo=o.OrderId,status="计划风险",reason=o.Reason})},Json);
        root["metrics"]=JsonSerializer.SerializeToNode(new[]{new{label="当前范围订单",value=orders.Length,unit="单",available=true,filter="all-orders"},new{label="锁定模拟任务",value=tasks.Count(t=>t.locked),unit="条",available=true,filter="all-tasks"},new{label="新增待执行任务",value=tasks.Count(t=>!t.locked),unit="条",available=true,filter="all-tasks"}},Json);
        root["status"]=JsonSerializer.SerializeToNode(new[]{new{label="已开工锁定作业",value=tasks.Count(t=>t.locked),key="run",tone="blue"},new{label="新增待执行",value=tasks.Count(t=>!t.locked),key="created",tone="green"}},Json);
        DecorateDispatch(root,p,tasks.ToDictionary(t=>t.taskGuid,t=>JsonSerializer.SerializeToElement(t,Json)));
        if(p.ReleaseStatus=="released") {root["detailGroups"]!["unknown"]=new JsonArray();root["orderFlow"]!["unknown"]=0;root["orderFlow"]!["simulatedPending"]=orders.Count(o=>!o.IsLocked);}
        return result;
    }
    private static void DecorateDispatch(JsonNode? node,DemoPlanState p,Dictionary<string,JsonElement> tasks)
    {
        if(node is JsonObject obj)
        {
            if(obj["rowType"]?.GetValue<string>()=="order")
            {
                var locked=CoupledDemoEngine.LockedRemaining(obj["orderNo"]!.GetValue<string>())>0;
                obj["releaseStatus"]=p.ReleaseStatus=="released"?"已模拟下达":"未下达（模拟）";
                obj["status"]=p.ReleaseStatus=="released"?"模拟任务已生成，待执行":"未下达，尚无任务";
                obj["statusKey"]=p.ReleaseStatus=="released"?"simulated_released":"task_not_generated";
                obj["taskStatus"]=p.ReleaseStatus=="released"?"待执行（模拟）":"未生成";
                obj["risk"]=p.ReleaseStatus=="released"?"normal":"attention";
                obj["reason"]=p.ReleaseStatus=="released"?"任务由本计算计划派生，实际报工为0；未确认现场开工。":"本模拟版本未下达，尚未生成本版本任务。";
                if(locked){obj["status"]="已开工订单 · 锁定模拟作业";obj["statusKey"]="locked_in_progress";obj["taskStatus"]="执行约束已锁定";obj["reason"]="原订单已开工；剩余量、物料锁定和后续任务为明确模拟输入，调序不会改变。";obj["releaseStatus"]="既有锁定模拟任务保留";obj["risk"]="normal";}
            }
            if(obj["rowType"]?.GetValue<string>()=="task"&&obj["id"] is JsonValue v&&v.TryGetValue<string>(out var id)&&tasks.TryGetValue(id!,out var task))
            {
                obj["status"]="待执行（模拟）";obj["taskStatus"]="待执行（模拟）";obj["statusKey"]="created";obj["reason"]="由当前已确认计划派生的本地模拟任务，实际报工为0。";
                obj["releaseStatus"]="已模拟下达 · 未向现场下发";
                var locked=task.GetProperty("locked").GetBoolean();if(locked){obj["statusKey"]="run";obj["status"]="已开工锁定作业（模拟）";obj["taskStatus"]="已开工锁定作业（模拟）";obj["reason"]="剩余量及后续作业是模拟输入，保留物料和产能占用；调序不改变任务。";obj["releaseStatus"]="既有锁定模拟任务";}obj["locked"]=locked;
                obj["sourceCalculationId"]=task.GetProperty("sourceCalculationId").GetString();obj["currentCalculationId"]=task.GetProperty("currentCalculationId").GetString();obj["sourcePlanRecordIds"]=JsonNode.Parse(task.GetProperty("sourcePlanRecordIds").GetRawText());
            }
            foreach(var kv in obj.ToArray())if(kv.Value is JsonObject or JsonArray)DecorateDispatch(kv.Value,p,tasks);
        }
        else if(node is JsonArray arr)foreach(var child in arr)DecorateDispatch(child,p,tasks);
    }
    private static void Attach(ToolResult result,DemoPlanState p,string id,InteractiveDemoTools.ScheduleResult run)
    {
        foreach(var block in result.UiPayload)
        {
            var spec=JsonSerializer.SerializeToNode(block.Spec,Json)!.AsObject();spec["demandVersion"]=InteractiveDemoTools.Version;spec["calculationId"]=run.CalculationId;spec["simulation"]=JsonSerializer.SerializeToNode(run.Simulation,Json);spec["workspace"]=JsonSerializer.SerializeToNode(Describe(p,id),Json);spec["headline"]=result.Summary;spec["limitations"]=JsonSerializer.SerializeToNode(InteractiveDemoTools.Rules,Json);
            if(spec["model"] is JsonObject model)model["limitations"]=JsonSerializer.SerializeToNode(InteractiveDemoTools.Rules,Json);
            block.Spec=spec;
        }
        result.AgentContext=new{source="synthetic-demo",demandVersion=InteractiveDemoTools.Version,calculationId=run.CalculationId,summary=result.Summary,asOf=InteractiveDemoTools.AsOf,originalApiInvoked=false};
    }
    private static string Show(string s)=>s.Length==0?"未取得补足日期":s;
    private static ToolResult Fail(string message)=>new(){Success=false,Summary=message,ErrorMessage=message};
}

