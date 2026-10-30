using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Agents;

// Check a completed native reply before it reaches the user. The model still
// selects tools and writes the reply; unsafe wording falls back to current facts.
public static class NativeReplyGuard
{
    const string OrderPattern=@"Z\d{7,9}(?!\d)";
    static string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)?v.ToString():"";
    static bool Flag(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.True;
    static JsonElement[] Rows(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().ToArray():Array.Empty<JsonElement>();
    static double Num(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDouble():0;
    static string N(double n)=>n.ToString("0.####",CultureInfo.InvariantCulture);
    public static string Present(string draft,IEnumerable<UiBlock> blocks,string question)
    {
        var native=blocks.Select(b=>JsonSerializer.SerializeToElement(b.Spec,new JsonSerializerOptions(JsonSerializerDefaults.Web))).FirstOrDefault(s=>Flag(s,"native"));
        if(native.ValueKind==JsonValueKind.Undefined)return draft;
        var view=Text(native,"view");var orders=Rows(native,"orders");var ids=orders.Concat(Rows(native,"availableOrders")).Select(o=>Text(o,"orderId")).ToHashSet();
        var bad=string.IsNullOrWhiteSpace(draft)||draft.Length>220
            ||Regex.IsMatch(draft,@"大模型|确定性|回滚|Submit|SIM-|GUID|本地|隔离|冻结|演示|模拟|测试版本|native|需求版本|只使用本次|[a-fA-F0-9]{32}")
            ||Regex.IsMatch(draft,@"(?m)^\s*[-*#]\s|最突出|最严重|最高|其余.*依次顺排|优先排产|全部完成|全部完工|已完工|一定准交|不可能.*交|已严重逾期|已经.*逾期|保证.*交付")
            ||Regex.Matches(draft,OrderPattern).Count>3
            ||Regex.Matches(draft,OrderPattern).Any(m=>!ids.Contains(m.Value));
        // Numbers and business status must retain their object/field relationship.
        // A set of allowed numbers would still accept swapped stock/remaining values.
        // Keep navigation prose; render factual claims from the typed tool result below.
        var prose=Regex.Replace(draft,OrderPattern+@"|[A-Za-z]\.\d+(?:\.\d+)*","");
        bad|=Regex.IsMatch(prose,@"\d|[零一二三四五六七八九十百千万两]+\s*(?:张|条|单|件|公斤|千克|吨|天|位)|计划|交期|风险|库存|在途|物料|材料|需求|抵扣|齐套|备齐|缺料|充足|开工|完工|交货|交付|超期|延期|推迟|提前|准交|下发|下达|采用|保存|运行|关闭|完成|任务量|未查到|没查到|没有记录");
        var days=orders.SelectMany(o=>new[]{Text(o,"planStart"),Text(o,"planEnd"),Text(o,"dueDate")})
            .Concat(Rows(native,"mrpRows").Select(r=>Text(r,"needDate"))).ToHashSet();
        bad|=Regex.Matches(draft,@"20\d{2}-\d{2}-\d{2}").Any(m=>!days.Contains(m.Value));
        if(view=="release")bad|=Regex.IsMatch(draft,@"主记录|计划明细");
        if(Flag(native,"prepare"))bad|=Regex.IsMatch(draft,@"已(?:把|将|完成|调|重排|计算|下发|试排)|(?:试排|排程|计算|核算).*(?:预览|结果).*已生成|已生成.*(?:预览|试排|结果)|预览已生成|(?:试排|重排|排程|核算|计算|下发|提交).{0,12}(?:完成|成功|生效)");
        if((view is "reschedule" or "insert" or "mrp")&&!Flag(native,"prepare"))bad|=Regex.IsMatch(draft,@"已(?:经)?(?:采用|保存)|采购已下单|(?:采用|保存)(?:已)?(?:完成|成功|生效)|自动(?:采用|保存)|无需确认");
        if(view=="insert")bad|=Regex.IsMatch(draft,@"(?:已|已经)[^。！？；\n]*(?:插入|加入|纳入)[^。！？；\n]*计划|插单[^。！？；\n]*(?:生效|完成)")||(Flag(native,"prepare")&&Regex.IsMatch(draft,@"已(?:经)?(?:插入|加入|纳入)"));
        if(Regex.IsMatch(draft,@"负载|理论工时|年月日|日期视图"))
            bad|=!Regex.IsMatch(question,@"负载|理论工时|年月日|日期视图")||Regex.IsMatch(draft,@"(?:已展示|已提供|支持).*(?:负载|理论工时|年月日|日期视图)|(?:负载|理论工时|年月日|日期视图).*(?:已展示|已提供)");
        // Only a small navigation grammar may bypass typed fact rendering.
        // Unknown assertions must not become safe merely by avoiding keywords.
        var navigationOnly=Regex.IsMatch(draft.Trim(),@"\A(?:请|可以|可)?(?:展开|继续)?(?:查看|核对)(?:下方)?(?:卡片|明细|详情|表格)[。！]?\z");
        var foundOrder=Regex.Match(draft.Trim(),@"\A(?<id>"+OrderPattern+@")的工序安排已查到，可以展开查看。\z");
        var verifiedOrderNavigation=view=="order"&&orders.Length==1&&foundOrder.Success
            &&foundOrder.Groups["id"].Value==Text(orders[0],"orderId")
            &&Text(orders[0],"planStart").Length>0&&Text(orders[0],"planEnd").Length>0
            &&native.TryGetProperty("chart",out var chart)&&chart.ValueKind==JsonValueKind.Object&&Rows(chart,"items").Length>0;
        bad|=!navigationOnly&&!verifiedOrderNavigation;
        if(!bad)return draft.Trim();
        var id=Text(native,"orderId");
        var count=native.TryGetProperty("allOrders",out var all)&&all.ValueKind==JsonValueKind.Array?all.GetArrayLength():orders.Length;
        if(Flag(native,"prepare"))return view switch{
            "insert"=>$"{(id.Length>0?$"先核对一下急单{id}":"先选一下新来的急单")}。我会把它和现有{count}张订单一起试排，共{count+1}张，看看交期和其他订单的影响。你确认后再开始，当前计划暂时不变。",
            "mrp"=>$"这次会核算本批{count}张订单{(id.Length>0?$"，先查看{id}":"")}。请核对后确认开始，生产计划保持不变。",
            "release"=>$"已整理好这{count}张订单的下发信息。请核对订单范围和计划日期，确认后再继续。",
            _=>id.Length>0?$"准备将{id}调整到输入顺序第{Num(native,"position")}位，对本批{count}张订单重新试排。请核对后确认，当前计划尚未改变。":$"请先选择目标订单和调整后的顺序，确认后对本批{count}张订单一起试排。"
        };
        if(view is "mrp" or "material")
        {
            var materials=Rows(native,"mrpRows");var terminal=materials.Where(r=>Text(r,"active").Length==0).ToArray();
            var row=(terminal.Length>0?terminal:materials).FirstOrDefault();
            if(row.ValueKind==JsonValueKind.Undefined)return Rows(native,"mrpPendingOrderIds").Length>0?"新加入的订单尚未完成独立物料核算，请按当前整批订单重新核算；计划用料可在明细中对照。":"当前范围未查到可核对的物料结果。";
            var unit=Text(row,"unit");if(unit=="KG")unit="公斤";
            return $"{Text(row,"orderId")}的{Text(row,"material")}需求为{N(Num(row,"beforeOffset"))}{unit}，抵扣库存{N(Num(row,"stockUsed"))}和在途{N(Num(row,"transitUsed"))}后，还需{N(Num(row,"remaining"))}{unit}。"+(view=="mrp"?"核对后可保存本次核算，生产计划保持不变。":"完整分解和抵扣记录可查看明细。");
        }
        if(view=="insert")
        {
            var target=orders.FirstOrDefault(o=>Text(o,"orderId")==id);var finish=target.ValueKind!=JsonValueKind.Undefined?Text(target,"planEnd"):"";
            return (finish.Length>0?$"这张急单本次计划排到{finish}。":"这张急单本次还没有排入计划。")+"你可以展开对照，看看其他订单有没有推迟，确认后再决定是否采用；当前仍保留原方案。";
        }
        if(view=="reschedule")return $"已完成本批{Rows(native,"changes").Length}张订单的试排，{id}调整到输入顺序第{Num(native,"position")}位。请比较前后变化再决定是否采用；本次变化也包含重算差异。";
        if(view=="release")return $"这{orders.Length}张订单的计划已下发，已有任务保留。可以继续核对订单排程和派工情况。";
        if(view=="dispatch"){var tasks=Rows(native,"tasks");return tasks.Length==0?"本次未查到关联任务，不能据此判断从未派工。":$"查到{tasks.Length}条关联任务，其中{tasks.Count(t=>Text(t,"state_type")=="run")}条运行中。可展开核对工序、产线和已报数量。";}
        if(view=="order"&&orders.Length==1){var o=orders[0];return $"{Text(o,"orderId")}计划安排在{Text(o,"planStart")}至{Text(o,"planEnd")}，交期为{Text(o,"dueDate")}。"+(Flag(o,"risk")?"计划结束晚于交期，需要关注。":"当前计划结束未超过交期。");}
        if(view=="risk")return $"查到{orders.Length}张存在计划交期风险的订单。可以在明细中逐单核对计划日期与交期。";
        return $"本批{orders.Length}张订单中，{orders.Count(o=>Text(o,"planEnd").Length>0)}张已有计划，{orders.Count(o=>Flag(o,"risk"))}张存在交期风险。可查看明细继续核对。";
    }
}
