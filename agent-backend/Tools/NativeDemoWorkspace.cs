using System.Globalization;
using System.Text.Json;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;

public static class NativeDemoWorkspace
{
    public static string Version=>Text(NativeDemoGateway.Config,"demandVersion");
    public static string BusinessDate=>Text(NativeDemoGateway.Config,"businessDate");
    public static string[] Ids=>NativeDemoGateway.Config.GetProperty("baselineOrderIds").EnumerateArray().Select(x=>x.GetString()!).ToArray();
    public static JsonElement[] InsertionOrders=>Rows(NativeDemoGateway.Config,"insertionOrders");
    public static string[] AllIds=>Ids.Concat(InsertionOrders.Select(o=>Text(o,"orderId"))).ToArray();
    public static bool Contains(string id)=>AllIds.Contains((id??"").Trim().ToUpperInvariant());
    static string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)?v.ValueKind==JsonValueKind.String?v.GetString()??"":v.ToString():"";
    static double Num(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDouble():0;
    static JsonElement[] Rows(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().ToArray():Array.Empty<JsonElement>();
    static string Day(JsonElement e,string key){var s=Text(e,key);return s.Length>=10&&string.CompareOrdinal(s[..10],"1900-01-02")>0?s[..10]:"";}
    static string Unit(JsonElement e,string code)=>Rows(e,"units").Where(x=>Text(x,"unit_no")==code).Select(x=>Text(x,"unit_name_cn")).FirstOrDefault()??code;
    static bool Linked(JsonElement task,HashSet<string> guids)=>Text(task,"sale_order_guid").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Any(guids.Contains);
    static HashSet<string>? MaterialCodes(JsonElement run,JsonElement mrp,string filter)
    {
        if(string.IsNullOrWhiteSpace(filter))return null;
        return Rows(run,"materials").Concat(Rows(mrp,"mrp")).Where(x=>Text(x,"material_no").Contains(filter,StringComparison.OrdinalIgnoreCase)||Text(x,"product_name").Contains(filter,StringComparison.OrdinalIgnoreCase)).Select(x=>Text(x,"material_no")).ToHashSet();
    }
    static (DateOnly start,DateOnly end) Window(JsonElement run,string from,string to)
    {
        var dates=Rows(run,"plans").Select(x=>Day(x,"end_date")).Where(x=>x.Length>0).OrderByDescending(x=>x).ToArray();
        var start=from.Length>0?DateOnly.ParseExact(from,"yyyy-MM-dd"):DateOnly.Parse(Text(NativeDemoGateway.Config,"start"));
        var end=to.Length>0?DateOnly.ParseExact(to,"yyyy-MM-dd"):DateOnly.Parse(dates.FirstOrDefault()??Text(NativeDemoGateway.Config,"end"));
        if(to.Length==0&&end<start)end=start.AddDays(6);
        if(from.Length==0&&start>end)start=end.AddDays(-6);
        if(start>end||end.DayNumber-start.DayNumber>92)throw new InvalidOperationException("查询日期须在93天内且起止有效。");
        return(start,end);
    }
    static OrderRow[] SelectOrders(JsonElement run,JsonElement mrp,string view,string id,string filter,string from,string to)
    {
        var orders=Orders(run).Where(o=>(id.Length==0||o.OrderId==id)&&(view!="risk"||o.Risk));
        if(view=="material"&&filter.Length>0)
        {
            var codes=MaterialCodes(run,mrp,filter)!;
            var matches=Rows(run,"materials").Concat(Rows(mrp,"mrp")).Where(x=>codes.Contains(Text(x,"material_no"))).Select(x=>Text(x,"sale_order_no")).ToHashSet();
            orders=orders.Where(o=>matches.Contains(o.OrderId));
        }
        if(from.Length>0||to.Length>0)
        {
            var window=Window(run,from,to);var plans=Rows(run,"plans");
            orders=orders.Where(o=>plans.Any(x=>Text(x,"sale_order_no")==o.OrderId&&Day(x,"start_date").Length>0&&DateOnly.Parse(Day(x,"start_date"))<=window.end&&DateOnly.Parse(Day(x,"end_date"))>=window.start));
        }
        return orders.ToArray();
    }
    static string MrpFacts(JsonElement mrp,string id,HashSet<string>? materialCodes=null)
    {
        var rows=Rows(mrp,"mrp").Where(x=>(id.Length==0||Text(x,"sale_order_no")==id)&&(materialCodes==null||materialCodes.Contains(Text(x,"material_no")))).OrderByDescending(x=>Text(x,"bom_level")).ThenByDescending(x=>Num(x,"craft_sequence")).Take(id.Length>0?2:1);
        return string.Join("；",rows.Select(x=>$"{Text(x,"sale_order_no")}的物料{Text(x,"material_no")}：抵扣前需求{Num(x,"lack_num")+Num(x,"remain_occupy_num")+Num(x,"supply_order_occupy_num"):0.####}，库存抵扣{Num(x,"remain_occupy_num"):0.####}，在途抵扣{Num(x,"supply_order_occupy_num"):0.####}，剩余需求{Num(x,"lack_num"):0.####}，损耗后需求{Num(x,"demand_num"):0.####}{(Unit(mrp,Text(x,"unit_no"))=="KG"?"公斤":Unit(mrp,Text(x,"unit_no")))}"));
    }
    public static DemoPlanState Copy(DemoPlanState p)=>JsonSerializer.Deserialize<DemoPlanState>(JsonSerializer.Serialize(p,NativeDemoGateway.Json),NativeDemoGateway.Json)!;
    public static bool Valid(DemoPlanState p)
    {
        try
        {
            if(p.SchemaVersion!=4||p.DemandVersion!=Version||p.Revision<0||p.Revision>=int.MaxValue-1||p.OrderIds==null||p.OrderIds.Distinct().Count()!=p.OrderIds.Length||!Ids.All(p.OrderIds.Contains)||!p.OrderIds.All(AllIds.Contains)||p.DemandQuantities==null||p.DemandQuantities.Count!=0||p.MrpInputs==null||p.MrpInputs.Length!=0||p.SupplyScenario!="frozen"||p.ReleaseStatus is not ("unreleased" or "released"))return false;
            var run=NativeDemoGateway.Read(p.NativeResultId);
            if(Text(run,"fixtureId")!=Text(NativeDemoGateway.Config,"fixtureId")||Text(run,"action") is not ("baseline" or "reschedule" or "insert")||!run.GetProperty("inputOrderIds").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(p.OrderIds)||!Rows(run,"orders").Select(o=>Text(o,"sale_order_no")).ToHashSet().SetEquals(p.OrderIds))return false;
            var mrp=NativeDemoGateway.Read(p.NativeMrpResultId,true);
            if(Text(mrp,"action")!="mrp"||Text(mrp,"fixtureId")!=Text(NativeDemoGateway.Config,"fixtureId"))return false;
            if(p.ReleaseStatus=="released")
            {
                if(!NativeDemoGateway.Token(p.NativeReleaseId))return false;
                var release=NativeDemoGateway.Read(p.NativeReleaseId);
                if(Text(release,"action")!="release"||Text(release,"sourceResultId")!=Text(run,"resultId")||p.ReleasedCalculationId!=Text(run,"resultId"))return false;
            }
            else if(p.NativeReleaseId.Length>0||p.ReleasedCalculationId.Length>0)return false;
            return true;
        }
        catch(Exception e)when(e is IOException or JsonException or InvalidOperationException or KeyNotFoundException){return false;}
    }

    public record OrderRow(string OrderId,string Product,double Quantity,double OrderQuantity,string DueDate,string TaskTag,int Position,string PlanStart,string PlanEnd,bool Risk,string Reason);
    public static OrderRow[] Orders(JsonElement run)
    {
        var plans=Rows(run,"plans");return Rows(run,"orders").Select(o=>
        {
            var id=Text(o,"sale_order_no");var records=plans.Where(x=>Text(x,"sale_order_no")==id).ToArray();
            var start=records.Select(x=>Day(x,"start_date")).Where(x=>x.Length>0).OrderBy(x=>x).FirstOrDefault()??"";
            var end=records.Select(x=>Day(x,"end_date")).Where(x=>x.Length>0).OrderByDescending(x=>x).FirstOrDefault()??"";var due=Day(o,"delivery_date");
            var risk=end.Length>0&&due.Length>0&&string.CompareOrdinal(end,due)>0;var task=Text(o,"task_tag");
            var reason=(task=="T"?"有派工在制记录；":"")+(end.Length==0?"未查到本次计划记录":risk?"计划结束晚于交期":"计划结束未超过交期");
            return new OrderRow(id,Text(o,"product_name"),Num(o,"order_num"),Num(o,"order_num"),due,task,(int)Num(o,"order_sn"),start,end,risk,reason);
        }).ToArray();
    }
    public static readonly string[] Rules={
        $"本地模拟输入统一到{BusinessDate}，交期按原相对间隔平移。库存与采购数量为固定模拟资料，真实WBF/RPC未调用。",
        "计算执行提取的原存储过程，数据库无网络、无端口；计算和试下发均回滚。源SQL2012与本机SQL2022尚未做同环境等价验收。",
        "原过程在制优先及工序产线约束继续生效，不把在制订单整单永久锁死。候选产线并列可产生不同计划，前后变化不能全部解释为急单造成。",
        "主计划物料结果来自本次原编排；独立MRP来自其自身需求与抵扣过程。独立MRP保存不会自动改变主计划或周计划。",
        "库存、在途、单位用量、损耗和安全需求取冻结输入和原结果，不提供手选供应情境。跨工序、物料和单位不得直接相加。",
        "MRP库存按原过程合格状态及库存配置取数；末级无工序材料的采购抵扣读取运行中采购合同未交量。库存池与采购池字段可能多行重复，不可重复合计。",
        "下发承接已展示计划GUID，不重算、不按订单捏造任务。实际任务与计划分开核对；下发记录不等于现场开工。"
    };
    public static object Describe(DemoPlanState p,string id="",OrderRow[]? displayedOrders=null,string view="overview")
    {
        var run=NativeDemoGateway.Read(p.NativeResultId);return new{native=true,view,focusOrderId=id,demandVersion=Version,source="isolated-demo",calculationId=Text(run,"resultId"),mrpResultId=Text(NativeDemoGateway.Read(p.NativeMrpResultId,true),"resultId"),releaseStatus=p.ReleaseStatus,planLabel="原过程隔离计划 / 独立MRP",orders=displayedOrders??Orders(run).Where(o=>id.Length==0||o.OrderId==id).ToArray()};
    }
    public static ToolResult Query(DemoPlanState p,string view,string id="",string version="",string startDate="",string endDate="",string filter="")
    {
        if(!Valid(p))return Fail("当前隔离演示状态无效或结果未取得，请新建对话；未回退到旧模拟算法。");
        if(version.Length>0&&version!=Version&&version!="261016902")return Fail("版本与当前隔离演示不一致，未混用结果。");
        if(id.Length>0&&!p.OrderIds.Contains(id))return Fail(Contains(id)?"这张订单尚未加入当前计划，可以先准备急单插入试排。":"未查到这张订单，未替换成其他订单。");
        try
        {
            var run=NativeDemoGateway.Read(p.NativeResultId);var mrp=NativeDemoGateway.Read(p.NativeMrpResultId,true);
            var orders=SelectOrders(run,mrp,view,id,filter,startDate,endDate);
            var codes=view=="material"?MaterialCodes(run,mrp,filter):null;
            if(view=="material"&&filter.Length>0&&orders.Length==0)return Fail($"当前计划中未取得与物料“{filter}”匹配的记录，未返回其他物料或推断齐套状态。");
            var guids=Rows(run,"orders").Where(o=>orders.Any(x=>x.OrderId==Text(o,"sale_order_no"))).Select(o=>Text(o,"sale_order_guid")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tasks=Rows(run,"tasks").Where(t=>Linked(t,guids)&&(filter.Length==0||view!="dispatch"||Text(t,"product_line_no")==filter)).ToArray();
            var headline=view switch{
                "material"=>$"已核对{orders.Length}张订单的物料需求，可查看库存、在途抵扣和剩余需求。",
                "dispatch"=>$"查到{tasks.Length}条关联任务，其中{tasks.Count(t=>Text(t,"state_type")=="run")}条处于运行中。",
                "risk"=>$"当前范围有{orders.Length}张订单的计划结束日期晚于交期。",
                "order"=>orders.Length==1?$"{id}计划安排在{orders[0].PlanStart}至{orders[0].PlanEnd}。":"当前范围未查到计划记录。",
                _=>$"当前范围{orders.Length}张订单中，{orders.Count(o=>o.PlanEnd.Length>0)}张已有计划，{orders.Count(o=>o.Risk)}张存在交期风险。"};
            var facts=headline;
            if(view=="material")facts+=$"{MrpFacts(mrp,id,codes)}。物料核算与当前生产计划分别保存。";
            if(view=="material"&&orders.Any(o=>!Rows(mrp,"inputOrderIds").Any(v=>v.GetString()==o.OrderId)))facts+="新加入订单尚无已保存的独立物料核算，请按当前整批订单重新核算；下方计划用料来自当前排程。";
            if(id.Length>0&&orders.Length==1)facts+=$"{id}：计划{orders[0].PlanStart}至{orders[0].PlanEnd}，交期{orders[0].DueDate}。";
            else if(view is "risk" or "overview")facts+="已核对的关注订单："+string.Join("；",orders.Where(o=>o.Risk).OrderByDescending(o=>DateOnly.Parse(o.PlanEnd).DayNumber-DateOnly.Parse(o.DueDate).DayNumber).Take(3).Select(o=>$"{o.OrderId}计划结束{o.PlanEnd}，交期{o.DueDate}"))+"。";
            return Result(view,facts,BuildSpec(p,run,mrp,view,id,headline,startDate:startDate,endDate:endDate,filter:filter));
        }
        catch(Exception e)when(e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException){return Fail(e.Message);}
    }
    public static ToolResult Execute(string action,string id,int position,int quantity,string scenario,DemoPlanState p,string version)
    {
        if(!Valid(p))return Fail("当前方案信息有误，请新建对话后重试。");
        if(version.Length>0&&version!=Version)return Fail("这次操作与当前计划不一致，请重新准备。");
        if(id.Length>0&&!Contains(id))return Fail("未查到这张订单，未替换为其他订单。");
        if(action.Contains("mrp")&&id.Length>0&&!p.OrderIds.Contains(id))return Fail("这张订单尚未加入当前计划，请先加入并采用后再核算本批物料需求。");
        if(p.ReleaseStatus=="released"&&action!="current")return Fail("当前计划已下发，不能再修改或重复下发。后续调整需按原系统流程使用新的未下发需求版本。");
        if(action.Contains("mrp")&&(quantity!=0||scenario is not ("" or "frozen")))return Fail("需求量、库存和在途由已有业务数据读取，目前不能通过助手改量；本次未计算。");
        if(action.Contains("reschedule")&&(position<1||position>p.OrderIds.Length||id.Length>0&&!p.OrderIds.Contains(id)))return Fail($"调序须选择当前计划中的订单，输入顺序为第1至{p.OrderIds.Length}位。");
        if(action.Contains("insert")&&(position!=1||id.Length>0&&(!InsertionOrders.Any(o=>Text(o,"orderId")==id)||p.OrderIds.Contains(id))))return Fail("插单须选择尚未进入当前计划的新订单；已在计划中的订单可使用调序。");
        try
        {
            var before=NativeDemoGateway.Read(p.NativeResultId);var mrp=NativeDemoGateway.Read(p.NativeMrpResultId,true);
            var kind=action.Replace("prepare-","");
            if(action.StartsWith("prepare-"))
            {
                if(kind=="insert"&&!InsertionOrders.Any(o=>!p.OrderIds.Contains(Text(o,"orderId"))))return Fail("当前没有尚未加入计划的新订单，请勿重复插入。");
                var preparedSummary=kind=="insert"?$"{(id.Length>0?$"先核对急单{id}":"先选一下新来的急单")}。我会把它放到待排顺序最前面，和现有{p.OrderIds.Length}张订单一起试排，共{p.OrderIds.Length+1}张。先看看交期和其他订单的影响，当前计划暂时不变。":kind=="mrp"?$"将计算本批{p.OrderIds.Length}张订单的物料需求{(id.Length>0?$"，先查看{id}":"")}。确认后开始计算，生产计划保持不变。":kind=="release"?$"已整理好这{p.OrderIds.Length}张订单的下发信息，请核对计划范围和日期后确认。":$"将{id}调整到输入顺序第{position}位，并对本批{p.OrderIds.Length}张订单重新试排。请核对后确认。";
                return Result(kind,preparedSummary,BuildSpec(p,before,mrp,kind,id,preparedSummary,prepare:true,position:position));
            }
            if(kind=="current")return Query(p,"overview");
            if(kind is not ("mrp" or "reschedule" or "insert" or "release"))return Fail("未知隔离操作。");
            if(kind is "reschedule" or "insert"&&id.Length==0)return Fail("请先选择目标订单。");
            var proposed=Copy(p);proposed.Revision++;
            var after=before;JsonElement? receipt=null;
            if(kind=="mrp")
            {mrp=NativeDemoGateway.Calculate("mrp",p.OrderIds);proposed.NativeMrpResultId=Text(mrp,"resultId");}
            if(kind is "reschedule" or "insert")
            {
                var ids=p.OrderIds.Where(x=>x!=id).ToList();ids.Insert(position-1,id);proposed.OrderIds=ids.ToArray();
                after=NativeDemoGateway.Calculate(kind,proposed.OrderIds,controlOrderIds:p.OrderIds);proposed.NativeResultId=Text(after,"resultId");
            }
            if(kind=="release")
            {
                receipt=NativeDemoGateway.Calculate("release",p.OrderIds,Text(before,"resultId"));
                proposed.NativeReleaseId=Text(receipt.Value,"resultId");proposed.ReleasedCalculationId=Text(before,"resultId");proposed.ReleaseStatus="released";
            }
            var summary=kind=="insert"?$"已将新订单{id}加入试排，订单由{p.OrderIds.Length}张增加到{proposed.OrderIds.Length}张。请查看急单安排及其他订单的变化，确认后再采用；当前计划尚未改变。":kind=="mrp"?$"本批{p.OrderIds.Length}张订单的物料需求已计算完成。{MrpFacts(mrp,id)}。核对后可保存本次结果，生产计划保持不变。":kind=="release"?$"这{p.OrderIds.Length}张订单的计划已下发，生成{Num(receipt!.Value.GetProperty("release"),"masterRows")}条主记录、{Num(receipt.Value.GetProperty("release"),"detailRows")}条计划明细。原有任务保留，未新增派工任务。":$"已完成本批{p.OrderIds.Length}张订单的试排，{id}调整到输入顺序第{position}位。请查看前后变化，确认后再采用。本次对比仅反映试排结果，不能将全部变化归因于调序。";
            if(kind=="insert")
            {
                var target=Orders(after).Single(o=>o.OrderId==id);var oldOrders=Orders(before).ToDictionary(o=>o.OrderId);
                var existing=Orders(after).Where(o=>oldOrders.ContainsKey(o.OrderId)&&o.PlanEnd.Length>0&&oldOrders[o.OrderId].PlanEnd.Length>0).ToArray();
                var earlier=existing.Count(o=>string.CompareOrdinal(o.PlanEnd,oldOrders[o.OrderId].PlanEnd)<0);var later=existing.Count(o=>string.CompareOrdinal(o.PlanEnd,oldOrders[o.OrderId].PlanEnd)>0);
                summary=target.PlanEnd.Length>0?$"这张急单本次计划排到{target.PlanEnd}，交期是{target.DueDate}。原有订单中，{earlier}单结束提前、{later}单结束延后。你可以展开对照，看完影响后再决定是否采用；当前仍保留原方案。":"这张急单本次还没有排入计划。可以先看明细核对，当前仍保留原方案。";
            }
            return Result(kind,summary,BuildSpec(p,after,mrp,kind,id,summary,position:position,before:before,proposed:proposed,receipt:receipt));
        }
        catch(Exception e)when(e is IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException){return Fail(e.Message);}
    }
    static object BuildSpec(DemoPlanState p,JsonElement run,JsonElement mrp,string view,string id,string summary,bool prepare=false,int position=1,JsonElement? before=null,DemoPlanState? proposed=null,JsonElement? receipt=null,string startDate="",string endDate="",string filter="")
    {
        var all=Orders(run);var orders=SelectOrders(run,mrp,view,view is "reschedule" or "insert"?"":id,filter,startDate,endDate);var matchedOrders=orders.Select(o=>o.OrderId).ToHashSet();
        var codes=view=="material"?MaterialCodes(run,mrp,filter):null;
        var mrpRows=Rows(mrp,"mrp").Where(x=>matchedOrders.Contains(Text(x,"sale_order_no"))&&(codes==null||codes.Contains(Text(x,"material_no")))).Select(x=>new{
            orderId=Text(x,"sale_order_no"),material=Text(x,"material_no"),name=Text(x,"product_name"),unit=Unit(mrp,Text(x,"unit_no")),level=Text(x,"bom_level"),active=Text(x,"active_no"),craftSequence=Num(x,"craft_sequence"),rate=Num(x,"material_rate"),orderDemand=Num(x,"undelivery_num"),stockPool=Num(x,"remain_num"),stockUsed=Num(x,"remain_occupy_num"),transitPool=Num(x,"supply_order_num"),transitUsed=Num(x,"supply_order_occupy_num"),remaining=Num(x,"lack_num"),demand=Num(x,"demand_num"),scrapRate=Num(x,"scrap_rate"),scrapNum=Num(x,"scrap_num"),safeDemand=Num(x,"safe_demand_num"),beforeOffset=Num(x,"lack_num")+Num(x,"remain_occupy_num")+Num(x,"supply_order_occupy_num"),needDate=Day(x,"delivery_date"),detailGuid=Text(x,"detail_guid")}).ToArray();
        var materialRows=Rows(run,"materials").Where(x=>matchedOrders.Contains(Text(x,"sale_order_no"))&&(codes==null||codes.Contains(Text(x,"material_no")))).ToArray();
        var selected=orders.Select(o=>o.OrderId).ToHashSet();var sourceOrders=Rows(run,"orders");var guids=sourceOrders.Where(x=>selected.Contains(Text(x,"sale_order_no"))).Select(x=>Text(x,"sale_order_guid")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tasks=Rows(run,"tasks").Where(x=>Linked(x,guids)&&(view!="dispatch"||filter.Length==0||Text(x,"product_line_no")==filter)).Select(x=>{
            var row=x.EnumerateObject().ToDictionary(v=>v.Name,v=>(object?)v.Value);
            var linked=Text(x,"sale_order_guid").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            row["orderIds"]=sourceOrders.Where(o=>linked.Contains(Text(o,"sale_order_guid"))).Select(o=>Text(o,"sale_order_no")).Distinct().ToArray();
            return row;
        }).ToArray();
        var old=before.HasValue?Orders(before.Value):all;var changes=all.Select(o=>{var b=old.SingleOrDefault(x=>x.OrderId==o.OrderId);return new{orderId=o.OrderId,taskTag=o.TaskTag,added=b==null,beforePosition=b?.Position,afterPosition=o.Position,beforeStart=b?.PlanStart,afterStart=o.PlanStart,beforeEnd=b?.PlanEnd,afterEnd=o.PlanEnd,dueDate=o.DueDate,beforeRisk=b?.Risk,afterRisk=o.Risk,reason=o.Reason};}).ToArray();
        var availableOrders=InsertionOrders.Where(o=>!p.OrderIds.Contains(Text(o,"orderId"))).Select(o=>new{orderId=Text(o,"orderId"),product=Text(o,"productNo"),quantity=Num(o,"quantity"),dueDate=Text(o,"dueDate"),taskTag="F"}).ToArray();
        var mrpPendingOrderIds=all.Where(o=>!Rows(mrp,"inputOrderIds").Any(v=>v.GetString()==o.OrderId)).Select(o=>o.OrderId).ToArray();
        var control=Rows(run,"controlPlans");var controlChanged=before.HasValue&&control.Length>0?ChangedOrders(Rows(before.Value,"plans"),control):-1;
        return new{native=true,presentationVersion=2,capabilities=new{operationGantt=true,lineLoad=false,dateViews=false},view,prepare,action=view,source="isolated-demo",sourceLabel="本地模拟 · 统一日期输入 · 原过程计算",demandVersion=Version,sourceVersion="261016902",asOf=BusinessDate+"T09:00:00+08:00",orderId=id,position,headline=summary,calculationId=Text(run,"resultId"),mrpResultId=Text(mrp,"resultId"),mrpSource="独立MRP",mrpSourceVersion="",mrpSourceLabel="同批模拟销售需求重新提取 → 独立MRP分解（本次未编号）",fixedDemoInputs=run.TryGetProperty("fixedDemoInputs",out var fixedInputs)?(JsonElement?)fixedInputs:null,releaseStatus=p.ReleaseStatus,baseRevision=p.Revision,baseState=p,proposedPlan=proposed,orders,allOrders=all,availableOrders,beforeOrderCount=old.Length,afterOrderCount=all.Length,mrpPendingOrderIds,mrpRows,materialRows,tasks,changes,controlChangedOrders=controlChanged,receipt,workspace=Describe(proposed??p,id,orders,view),chart=Chart(run,selected,startDate,endDate),rules=Rules};
    }
    static int ChangedOrders(JsonElement[] before,JsonElement[] after)
    {
        string Key(JsonElement p)=>string.Join("|",new[]{"sale_order_no","product_no","product_line_no","active_no","craft_sequence","start_date","end_date","shift_no","active_sn","plan_num","made_time","change_time"}.Select(k=>Text(p,k)));
        return before.Concat(after).Select(x=>Text(x,"sale_order_no")).Distinct().Count(id=>!before.Where(x=>Text(x,"sale_order_no")==id).Select(Key).OrderBy(x=>x).SequenceEqual(after.Where(x=>Text(x,"sale_order_no")==id).Select(Key).OrderBy(x=>x)));
    }
    static object Chart(JsonElement run,HashSet<string> selected,string from,string to)
    {
        var plans=Rows(run,"plans").Where(x=>selected.Contains(Text(x,"sale_order_no"))).ToArray();
        var (start,end)=Window(run,from,to);
        var columns=Enumerable.Range(0,end.DayNumber-start.DayNumber+1).SelectMany(n=>new[]{"白班","晚班"}.Select(shift=>new{date=start.AddDays(n).ToString("yyyy-MM-dd"),weekday=CultureInfo.GetCultureInfo("zh-CN").DateTimeFormat.GetDayName(start.AddDays(n).DayOfWeek),shift})).ToArray();
        string Group(JsonElement p)=>string.Join("|",new[]{"sale_order_no","product_line_no","active_no","craft_sequence"}.Select(k=>Text(p,k)));
        var groups=plans.GroupBy(Group).ToArray();var rows=groups.Select(g=>new{key=g.Key,orderId=Text(g.First(),"sale_order_no"),lineName=Text(g.First(),"product_line_no"),stage=Text(g.First(),"active_no"),productName=Text(g.First(),"product_no"),planQuantity=g.Sum(x=>Num(x,"plan_num")),planStart=g.Min(x=>Day(x,"start_date")),planEnd=g.Max(x=>Day(x,"end_date"))}).ToArray();
        var items=plans.Where(x=>DateOnly.Parse(Day(x,"start_date"))>=start&&DateOnly.Parse(Day(x,"start_date"))<=end).Select(x=>new{id=Text(x,"plan_guid"),rowIndex=Array.FindIndex(groups,g=>g.Key==Group(x)),columnIndex=(DateOnly.Parse(Day(x,"start_date")).DayNumber-start.DayNumber)*2+(Text(x,"shift_no")=="day"?0:1),date=Day(x,"start_date"),shift=Text(x,"shift_no")=="day"?"白班":"晚班",orderId=Text(x,"sale_order_no"),stage=Text(x,"active_no"),operation=Text(x,"active_no"),lineId=Text(x,"product_line_no"),quantity=Num(x,"plan_num"),plannedHours=Num(x,"made_time"),tone=Text(x,"shift_no")=="day"?"day":"night"}).ToArray();
        return new{kind="gantt",title="原过程隔离排程",columns,rows,items,emptyText="当前范围没有取得计划记录"};
    }
    static ToolResult Result(string view,string summary,object spec)
    {
        var data=JsonSerializer.SerializeToElement(spec,NativeDemoGateway.Json);
        var prepare=data.TryGetProperty("prepare",out var p)&&p.GetBoolean();
        var title=prepare?view switch{"insert"=>"生产计划调整确认","mrp"=>"物料需求计算确认","release"=>"计划下发确认",_=>"订单顺序调整确认"}
            :view switch{"insert"=>"生产计划总览","mrp"=>"物料需求计算结果","material"=>"订单物料核对","dispatch"=>"派工情况","risk"=>"交期风险","overview"=>"生产计划总览","order"=>"订单排程","release"=>"计划下发完成",_=>"试排结果对比"};
        return new(){Success=true,Summary=summary,AgentContext=new{view,capabilities=new{operationGantt=true,lineLoad=false,dateViews=false}},UiPayload=new(){new(){Type="interactive-demo",Title=title,Spec=spec}}};
    }
    static ToolResult Fail(string message)=>new(){Success=false,Summary=message,ErrorMessage=message};
}
