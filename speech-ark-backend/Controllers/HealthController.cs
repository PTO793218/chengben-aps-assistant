using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WePilot.Speech.Options;

namespace WePilot.Speech.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get([FromServices] IOptions<ArkSpeechOptions> options)
    {
        return Ok(new
        {
            status = "ok",
            service = "speech-ark-backend",
            configured = !string.IsNullOrWhiteSpace(options.Value.ApiKey),
            asrMode = "volcengine-bigmodel-async",
            ttsMode = "volcengine-unidirectional-http"
        });
    }
}
