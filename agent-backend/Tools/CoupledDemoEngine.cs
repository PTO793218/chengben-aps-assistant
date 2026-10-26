using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WePilot.Agent.Tools;

// Pure calculation over an immutable local scenario. Re-running never consumes a lot.
public static class CoupledDemoEngine
{
    private static readonly JsonElement Data = Load();
    public static string DataVersion => Data.GetProperty("dataVersion").GetString()!;
    public static decimal Rate => Data.GetProperty("steelRate").GetDecimal();
    public const string LockedCalculationId="SIM-LOCKED-20261015";
    public static readonly DateOnly Start = DateOnly.Parse(Data.GetProperty("start").GetString()!);
    public static readonly DateOnly End = DateOnly.Parse(Data.GetProperty("end").GetString()!);
    public static string[] Stages => Data.GetProperty("stages").EnumerateArray().Select(x=>x.GetProperty("name").GetString()!).ToArray();
    public static int[] Capacities => Data.GetProperty("stages").EnumerateArray().Select(x=>x.GetProperty("capacity").GetInt32()).ToArray();
    public static string[] LockedIds => Data.GetProperty("lockedRemainingQuantities").EnumerateObject().Select(p=>p.Name).ToArray();
    public static int LockedRemaining(string id) => Data.GetProperty("lockedRemainingQuantities").TryGetProperty(id,out var value)?value.GetInt32():0;
    public static decimal ReservedStock => LockedIds.Sum(id=>Math.Round(LockedRemaining(id)*Rate,2,MidpointRounding.AwayFromZero));
    public static DemoPlanState Copy(DemoPlanState p) => new() {SchemaVersion=3,DemandVersion=p.DemandVersion,Revision=p.Revision,OrderIds=p.OrderIds.ToArray(),DemandQuantities=new(p.DemandQuantities),SupplyScenario=p.SupplyScenario,ReleaseStatus=p.ReleaseStatus,ReleasedCalculationId=p.ReleasedCalculationId};
    public static string CalculationId(DemoPlanState p)
    {
        var key=$"{DataVersion}|{p.DemandVersion}|{p.SupplyScenario}|{string.Join(",",p.OrderIds.Select(id=>$"{id}:{Quantity(p,id)}"))}";
        return "SIM-CALC-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..12];
    }
    public static int Quantity(DemoPlanState p,string id) {id=(id??"").Trim().ToUpperInvariant();return LockedRemaining(id)>0?LockedRemaining(id):p.DemandQuantities.TryGetValue(id,out var q)?q:InteractiveDemoTools.DefaultQuantity(id);}
    public static InteractiveDemoTools.ScheduleResult Run(DemoPlanState p)
    {
        var calculationId=CalculationId(p);
        var stock=p.SupplyScenario=="none"?ReservedStock:Data.GetProperty("stock").GetDecimal();var stockRemaining=stock;
        var lots=p.SupplyScenario is "supply" or "late"?Data.GetProperty("lots").EnumerateArray().Select(l=>new Lot(l.GetProperty("id").GetString()!,l.GetProperty("quantity").GetDecimal(),p.SupplyScenario=="late"&&l.TryGetProperty("lateArrival",out var late)?late.GetString()!:l.GetProperty("arrival").GetString()!)).OrderBy(l=>l.Arrival).ToArray():Array.Empty<Lot>();
        var remaining=lots.ToDictionary(l=>l.Id,l=>l.Quantity);var availability=new int[3];var tasks=new List<InteractiveDemoTools.DemoTask>();var orders=new List<InteractiveDemoTools.DemoOrder>();
        var capacities=Capacities;var stages=Stages;var horizon=(End.DayNumber-Start.DayNumber+1)*2;
        foreach(var id in p.OrderIds)
        {
            var source=InteractiveDemoTools.Order(id);var quantity=Quantity(p,id);var gross=Math.Round(quantity*Rate,2,MidpointRounding.AwayFromZero);
            var used=Math.Min(gross,stockRemaining);stockRemaining-=used;var shortage=gross-used;var allocations=new List<LotUse>();
            foreach(var lot in lots)
            {
                var take=Math.Min(shortage,remaining[lot.Id]);if(take<=0)continue;
                remaining[lot.Id]-=take;shortage-=take;allocations.Add(new(lot.Id,lot.Arrival,take));
            }
            var ready=shortage>0?"":allocations.Count>0?allocations.Max(l=>l.Arrival)!:Start.ToString("yyyy-MM-dd");
            var material=new MaterialAllocation(gross,used,allocations.Sum(l=>l.Quantity),shortage,ready,shortage>0?"shortage":allocations.Count>0?"waiting-inbound":"stock-ready",allocations.ToArray());
            var first=-1;var previousEnd=ready.Length==0?0:Math.Max(0,(DateOnly.Parse(ready).DayNumber-Start.DayNumber)*2);var complete=shortage==0;var hasTask=false;
            if(complete)for(var stage=0;stage<3;stage++)
            {
                var slot=Math.Max(previousEnd,availability[stage]);var left=quantity;
                while(left>0 && slot<horizon)
                {
                    var q=Math.Min(left,capacities[stage]);if(first<0)first=slot;hasTask=true;
                    tasks.Add(new(id,stage,stages[stage],slot,q,capacities[stage],Date(slot),slot%2==0?"白班":"晚班") {PlanRecordId=$"{(LockedRemaining(id)>0?LockedCalculationId:calculationId)}-{id}-S{stage+1}-{slot}"});left-=q;slot++;
                }
                previousEnd=slot;availability[stage]=slot;if(left>0){complete=false;break;}
            }
            var start=hasTask?Date(first):"";var end=complete?Date(previousEnd-1):"";
            var status=complete?"fullyScheduled":hasTask?"partiallyScheduled":"unscheduled";
            var risk=!complete || string.CompareOrdinal(end,source.DueDate)>0;
            var reason=shortage>0?$"缺料 {shortage:0.00} kg，尚无补足供应日期，未排入计划":!complete?"模拟计划窗口内产能不足，未排完整":risk?$"计划结束 {end} 晚于交期 {source.DueDate}":"当前模拟计划未超过交期";
            var locked=LockedRemaining(id)>0;
            orders.Add(source with {Quantity=quantity,OrderQuantity=source.Quantity,Position=orders.Count+1,QueuePosition=locked?0:orders.Count+1-LockedIds.Length,IsLocked=locked,ProductionStatus=locked?"inProgress":"notStarted",PlanStart=start,PlanEnd=end,Risk=risk,ScheduleStatus=status,Reason=(locked?"已开工订单：剩余量及后续锁定作业为模拟输入；":"")+reason,Material=material});
        }
        var simulation=new Simulation(orders.Count(o=>o.ScheduleStatus=="fullyScheduled"),orders.Count(o=>o.ScheduleStatus=="partiallyScheduled"),orders.Count(o=>o.ScheduleStatus=="unscheduled"),orders.Count(o=>o.Risk),orders.Sum(o=>o.Material!.GrossNeed),stock,lots.Sum(l=>l.Quantity),orders.Sum(o=>o.Material!.StockUsed),orders.Sum(o=>o.Material!.TransitUsed),orders.Sum(o=>o.Material!.PurchaseNeed),stockRemaining+remaining.Values.Sum(),ReservedStock,LockedIds.Length,orders.Count-LockedIds.Length);
        return new(orders.ToArray(),tasks.ToArray()){CalculationId=calculationId,Simulation=simulation,SupplyLots=lots};
    }
    public static string Date(int slot)=>Start.AddDays(slot/2).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
    public record Lot(string Id,decimal Quantity,string Arrival);
    public record LotUse(string SupplyOrderNo,string Arrival,decimal Quantity);
    public record MaterialAllocation(decimal GrossNeed,decimal StockUsed,decimal TransitUsed,decimal PurchaseNeed,string ReadyDate,string Status,LotUse[] Allocations);
    public record Simulation(int FullyScheduled,int PartiallyScheduled,int Unscheduled,int RiskCount,decimal GrossNeed,decimal Stock,decimal Transit,decimal StockUsed,decimal TransitUsed,decimal PurchaseNeed,decimal Unallocated,decimal ReservedStock,int LockedOrders,int PendingOrders);
    private static JsonElement Load(){using var stream=typeof(CoupledDemoEngine).Assembly.GetManifestResourceStream("WePilot.Agent.Data.coupled-demo.json")!;using var doc=JsonDocument.Parse(stream);return doc.RootElement.Clone();}
}
