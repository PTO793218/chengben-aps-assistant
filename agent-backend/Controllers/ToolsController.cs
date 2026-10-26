using Microsoft.AspNetCore.Mvc;
using WePilot.Agent.Tools;
namespace WePilot.Agent.Controllers;
[ApiController, Route("api/tools")]
public sealed class ToolsController : ControllerBase
{
    [HttpPost("interactive-demo")]
    public IActionResult InteractiveDemo([FromServices] WePilot.Agent.Tools.Results.ToolExecutionContext context, [FromBody] InteractiveDemoRequest request)
    {
        var result = new InteractiveDemoTools(context).Execute(request.Action, request.OrderId ?? "", request.Position, request.Quantity, request.Scenario, request.Plan, request.Version);
        return result.Success ? Ok(result) : BadRequest(result);
    }
    [HttpGet("production")]
    public IActionResult Production([FromServices] ProductionTools tools, [FromQuery] string? orderId = null,
        [FromQuery] string? lineId = null, [FromQuery] string? scope = null, [FromQuery] string? date = null)
    {
        var result = tools.Execute(orderId ?? "", lineId ?? "", scope ?? "all", date ?? "");
        return result.Success ? Ok(result) : BadRequest(result);
    }
    [HttpGet("aps-analysis")]
    public IActionResult ApsAnalysis([FromServices] ApsAnalysisTools tools, [FromQuery] string type = "overview",
        [FromQuery] string? orderId = null, [FromQuery] string? startDate = null, [FromQuery] string? endDate = null,
        [FromQuery] string? version = null, [FromQuery] string? source = null, [FromQuery] string? scheduleScope = null)
    {
        var result = tools.Execute(type, orderId ?? "", startDate ?? "", endDate ?? "", version ?? "", source ?? "", scheduleScope ?? "all");
        return result.Success ? Ok(result) : BadRequest(result);
    }
    [HttpGet("order-dispatch-status")]
    public IActionResult OrderDispatchStatus([FromServices] BusinessOrderDispatch tools, [FromQuery] string? lineId = null, [FromQuery] string? orderId = null)
    {
        var result = tools.Execute(lineId ?? "", orderId ?? "");
        return result.Success ? Ok(result) : BadRequest(result);
    }
    [HttpGet("material-readiness")]
    public IActionResult MaterialReadiness([FromServices] BusinessMaterialReadiness tools,
        [FromQuery] string scope = "all", [FromQuery] string? orderId = null,
        [FromQuery] string? materialQuery = null, [FromQuery] string? version = null)
    {
        var result = tools.Execute(scope, orderId ?? "", materialQuery ?? "", version ?? "");
        return result.Success ? Ok(result) : BadRequest(result);
    }
    [HttpGet("sample")]
    public IActionResult Sample([FromServices] SampleTools tools, [FromQuery] string chartType = "bar")
    {
        var result = tools.Execute(chartType);
        return result.Success ? Ok(result) : BadRequest(result);
    }
}

public sealed class InteractiveDemoRequest
{
    public string Action { get; set; } = "reschedule";
    public string? OrderId { get; set; }
    public int Position { get; set; } = 1;
    public int Quantity { get; set; } = 12500;
    public string Scenario { get; set; } = "stock";
    public string Version { get; set; } = InteractiveDemoTools.Version;
    public DemoPlanState? Plan { get; set; }
}
