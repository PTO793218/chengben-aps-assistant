using System.ComponentModel;
using Microsoft.SemanticKernel;
using WePilot.Agent.Tools.Results;

namespace WePilot.Agent.Tools;
public sealed class SampleTools
{
    private readonly ToolExecutionContext _context;
    public SampleTools(ToolExecutionContext context) => _context = context;
    [KernelFunction("get_sample_data"), Description("获取固定示例数据与图表。不是客户真实业务。chartType 支持 bar、line、pie。")]
    public string GetSampleData([Description("图表类型：bar、line、pie")] string chartType = "bar")
        => Execute(chartType).ToModelJson();
    public ToolResult Execute(string chartType)
    {
        var type = (chartType ?? "bar").ToLowerInvariant();
        ToolResult result;
        if (type is not ("bar" or "line" or "pie"))
            result = new() { Success = false, Summary = "图表类型不支持。", ErrorMessage = "请选择 bar、line 或 pie。" };
        else
        {
            var values = new[] { 32, 48, 41, 65, 58, 76 };
            var rows = values.Select((value, index) => new Dictionary<string, object?>
                { ["name"] = $"样例 {index + 1}", ["value"] = value }).ToList();
            result = new()
            {
                Success = true,
                Summary = "固定示例数据，共 6 项，合计 320；不代表真实业务。",
                AgentContext = new { source = "demo", total = values.Sum(), count = values.Length, rows },
                UiPayload = new() { new() { Type = "chart", Title = "示例数据", Spec = new ChartSpec
                    { ChartType = type, XField = "name", YField = "value", Data = rows } } }
            };
        }
        _context.ToolCalls.Add(new() { ToolName = "get_sample_data", Success = result.Success, Summary = result.Summary });
        _context.UiPayload.AddRange(result.UiPayload);
        return result;
    }
}
