using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WePilot.Speech.Contracts;
using WePilot.Speech.Options;
using WePilot.Speech.Services;

namespace WePilot.Speech.Controllers;

[ApiController, Route("api/speech")]
public sealed class SpeechController : ControllerBase
{
    private readonly ILogger<SpeechController> _logger;
    public SpeechController(ILogger<SpeechController> logger) => _logger = logger;

    [HttpGet("asr/stream")]
    public async Task StreamAsr([FromServices] IOptions<ArkSpeechOptions> options,
        [FromServices] VolcengineAsrProtocol protocol, CancellationToken cancellationToken)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest) { Response.StatusCode = 400; return; }
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey)) { Response.StatusCode = 503; return; }
        using var browser = await HttpContext.WebSockets.AcceptWebSocketAsync();
        using var ark = new ClientWebSocket();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        session.CancelAfter(TimeSpan.FromSeconds(90));
        var ct = session.Token;
        var connectId = Guid.NewGuid().ToString();
        ark.Options.SetRequestHeader("X-Api-Key", options.Value.ApiKey);
        ark.Options.SetRequestHeader("X-Api-Resource-Id", options.Value.AsrResourceId);
        ark.Options.SetRequestHeader("X-Api-Connect-Id", connectId);
        Task<bool>? upload = null;
        Task? download = null;
        try
        {
            await ark.ConnectAsync(new Uri(options.Value.AsrEndpoint), ct);
            await ark.SendAsync(protocol.CreateFullClientRequest(connectId), WebSocketMessageType.Binary, true, ct);
            await Send(browser, new BrowserAsrEvent("ready"), ct);
            upload = ForwardBrowser(browser, ark, protocol, ct);
            download = ForwardArk(ark, browser, protocol, ct);
            var first = await Task.WhenAny(upload, download);
            if (first == upload && await upload)
            {
                // A stop command sends the final audio packet; allow the last transcription to arrive.
                await Task.WhenAny(download, Task.Delay(TimeSpan.FromSeconds(8), ct));
                if (download.IsCompleted) await download;
            }
            else if (first == download) await download;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech ASR request {RequestId} failed.", HttpContext.TraceIdentifier);
            if (browser.State == WebSocketState.Open)
            {
                using var reportTimeout = new CancellationTokenSource(1000);
                try { await Send(browser, new BrowserAsrEvent("error", Code: "ASR_UPSTREAM", Message: "语音识别暂时不可用，请稍后重试。"), reportTimeout.Token); } catch { }
            }
        }
        finally
        {
            session.Cancel();
            try { if (upload is not null) await upload; } catch (Exception) when (ct.IsCancellationRequested) { }
            try { if (download is not null) await download; } catch (Exception) when (ct.IsCancellationRequested) { }
            ark.Abort();
            if (browser.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeTimeout = new CancellationTokenSource(1000);
                try { await browser.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Session ended", closeTimeout.Token); } catch { browser.Abort(); }
            }
        }
    }

    [HttpPost("tts/stream")]
    public async Task StreamTts([FromBody] TtsStreamRequest request,
        [FromServices] VolcengineTtsService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 10000)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "请输入 1 至 10000 字的播报内容。" }, cancellationToken);
            return;
        }
        try { await service.StreamAsync(request, Response, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Speech TTS request {RequestId} failed.", HttpContext.TraceIdentifier);
            if (Response.HasStarted) HttpContext.Abort();
            else
            {
                Response.StatusCode = 502;
                await Response.WriteAsJsonAsync(new { error = "语音播报暂时不可用，请检查服务配置。", requestId = HttpContext.TraceIdentifier }, cancellationToken);
            }
        }
    }

    private static async Task<bool> ForwardBrowser(WebSocket browser, WebSocket ark, VolcengineAsrProtocol protocol, CancellationToken ct)
    {
        while (browser.State == WebSocketState.Open && ark.State == WebSocketState.Open)
        {
            var (type, bytes) = await ReceiveMessage(browser, ct);
            if (type == WebSocketMessageType.Close) return false;
            if (type == WebSocketMessageType.Text)
            {
                using var command = JsonDocument.Parse(bytes);
                if (command.RootElement.TryGetProperty("type", out var name) && name.GetString() == "stop")
                {
                    await ark.SendAsync(protocol.CreateAudioPacket(Array.Empty<byte>(), true), WebSocketMessageType.Binary, true, ct);
                    return true;
                }
            }
            else await ark.SendAsync(protocol.CreateAudioPacket(bytes, false), WebSocketMessageType.Binary, true, ct);
        }
        return false;
    }

    private static async Task ForwardArk(WebSocket ark, WebSocket browser, VolcengineAsrProtocol protocol, CancellationToken ct)
    {
        while (ark.State == WebSocketState.Open && browser.State == WebSocketState.Open)
        {
            var (type, bytes) = await ReceiveMessage(ark, ct);
            if (type == WebSocketMessageType.Close) return;
            if (type != WebSocketMessageType.Binary) continue;
            foreach (var item in protocol.ParseServerPacket(bytes))
            {
                await Send(browser, item.Type == "error"
                    ? new BrowserAsrEvent("error", Code: item.Code, Message: "语音识别服务返回错误，请检查服务配置。") : item, ct);
                if (item.Type == "error") return;
            }
        }
    }

    private static async Task<(WebSocketMessageType Type, byte[] Bytes)> ReceiveMessage(WebSocket socket, CancellationToken ct)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[16384];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return (result.MessageType, Array.Empty<byte>());
            if (payload.Length + result.Count > 1024 * 1024) throw new InvalidDataException("Speech message is too large.");
            payload.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return (result.MessageType, payload.ToArray());
    }

    private static Task Send(WebSocket socket, BrowserAsrEvent item, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, ct);
    }
}
