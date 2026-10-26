using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WePilot.Agent.Tools;

// Only exchanges whitelisted local jobs. No SQL, business connection or Docker access here.
public static class NativeDemoGateway
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly JsonElement Config = Embedded("native-demo.json");
    public static readonly JsonElement Baseline = Embedded("native-baseline.json");
    public static readonly JsonElement MrpBaseline = Embedded("native-mrp-baseline.json");
    public static string Root => Environment.GetEnvironmentVariable("NativeDemo__RuntimePath") ?? "/demo-runtime";
    public static bool Token(string? value) => value != null && Regex.IsMatch(value,"\\A[a-f0-9]{32}\\z");
    public static JsonElement Embedded(string name)
    {
        using var stream=typeof(NativeDemoGateway).Assembly.GetManifestResourceStream("WePilot.Agent.Data."+name) ?? throw new InvalidOperationException("缺少隔离演示资料");
        using var doc=JsonDocument.Parse(stream);return doc.RootElement.Clone();
    }
    public static JsonElement Read(string? id,bool mrp=false)
    {
        var baseline=mrp?MrpBaseline:Baseline;
        if(string.IsNullOrEmpty(id)||id==baseline.GetProperty("resultId").GetString())return Checked(baseline);
        if(!Token(id))throw new InvalidOperationException("计算结果编号无效");
        var path=Path.Combine(Root,"results",id+".json");
        if(!File.Exists(path))throw new InvalidOperationException("未取得该计算结果，不能替换为其他计划");
        using var doc=JsonDocument.Parse(File.ReadAllText(path));var result=doc.RootElement.Clone();
        if(result.GetProperty("resultId").GetString()!=id||result.GetProperty("source").GetString()!="isolated-demo")throw new InvalidOperationException("计算结果来源不一致");
        return Checked(result);
    }
    static JsonElement Checked(JsonElement result)
    {
        if(!result.TryGetProperty("fixtureId",out var fixture)||fixture.GetString()!=Config.GetProperty("fixtureId").GetString()||
           !result.TryGetProperty("inputFingerprint",out var fingerprint)||fingerprint.GetString()!=Config.GetProperty("inputFingerprint").GetString())
            throw new InvalidOperationException("这条记录属于较早的数据，请新建对话使用当前计划；原记录保留。");
        return result;
    }
    public static JsonElement Calculate(string action,string[] orderIds,string resultId="",string[]? controlOrderIds=null)
    {
        var heartbeat=Path.Combine(Root,"heartbeat");
        if(!File.Exists(heartbeat)||!double.TryParse(File.ReadAllText(heartbeat),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds)||DateTimeOffset.UtcNow.ToUnixTimeSeconds()-seconds>30)
            throw new InvalidOperationException("本机隔离计算服务未就绪；当前计划保留，请启动本地演示服务后重试。");
        var id=Guid.NewGuid().ToString("N");var requests=Path.Combine(Root,"requests");Directory.CreateDirectory(requests);
        var input=new Dictionary<string,object>{{"action",action},{"orderIds",orderIds}};
        if(resultId.Length>0)input["resultId"]=resultId;
        if(controlOrderIds!=null)input["controlOrderIds"]=controlOrderIds;
        var temp=Path.Combine(requests,id+".tmp");File.WriteAllText(temp,JsonSerializer.Serialize(input,Json));File.Move(temp,Path.Combine(requests,id+".json"));
        var response=Path.Combine(Root,"responses",id+".json");var watch=System.Diagnostics.Stopwatch.StartNew();
        while(watch.Elapsed<TimeSpan.FromSeconds(150))
        {
            if(File.Exists(response))
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(response));var value=doc.RootElement;
                if(!value.GetProperty("success").GetBoolean())throw new InvalidOperationException("隔离计算未完成，当前方案保留："+(value.TryGetProperty("message",out var msg)?msg.GetString():"请查看本地记录"));
                return Read(id);
            }
            Thread.Sleep(150);
        }
        throw new InvalidOperationException("隔离计算超时，未采用任何结果；当前计划保留。");
    }
}
