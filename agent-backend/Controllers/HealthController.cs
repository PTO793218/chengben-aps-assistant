using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WePilot.Agent.Options;
using WePilot.Agent.Tools;
namespace WePilot.Agent.Controllers;
[ApiController, Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromServices] IOptions<SemanticKernelOptions> model, [FromServices] IConfiguration config)
        => Ok(new { status = "ok", service = "wepilot-agent", mode = config["Agent:Mode"] ?? "demo",
            configured = !string.IsNullOrWhiteSpace(model.Value.ApiKey) && !string.IsNullOrWhiteSpace(model.Value.ModelId),
            dataset = new { datasetId=NativeDemoGateway.DatasetId, demandVersion=NativeDemoWorkspace.Version,
                fixtureId=NativeDemoGateway.Config.GetProperty("fixtureId").GetString(), inputFingerprint=NativeDemoGateway.Config.GetProperty("inputFingerprint").GetString() } });
}
