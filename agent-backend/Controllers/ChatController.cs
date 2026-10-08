using Microsoft.AspNetCore.Mvc;
using WePilot.Agent.Agents;
using WePilot.Agent.Contracts;

namespace WePilot.Agent.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ChatAgentService _agent;

    public ChatController(ChatAgentService agent)
    {
        _agent = agent;
    }

    [HttpPost("stream")]
    public async Task StreamAsync([FromBody] ChatStreamRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        await _agent.StreamAsync(request, Response, cancellationToken);
    }
}
