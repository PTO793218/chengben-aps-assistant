using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WePilot.Agent.Options;

namespace WePilot.Agent.Infrastructure.Ai;

// One instance per KernelFactory scope. Reasoning never leaves this request's
// in-memory transport state; SK still owns tool execution and the fact guards.
public sealed class DeepSeekCompatibilityHandler : HttpMessageHandler
{
    readonly HttpClient _upstream;
    readonly bool _thinking;
    readonly string _effort;
    readonly ILogger<DeepSeekCompatibilityHandler>? _logger;
    readonly Dictionary<string, string> _toolReasoning = new();

    public DeepSeekCompatibilityHandler(HttpClient upstream, SemanticKernelOptions options,
        ILogger<DeepSeekCompatibilityHandler>? logger = null)
    {
        _upstream = upstream;
        _thinking = options.ThinkingEnabled;
        _effort = options.ReasoningEffort;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
        body["thinking"] = new JsonObject { ["type"] = _thinking ? "enabled" : "disabled" };
        if (_thinking)
        {
            body["reasoning_effort"] = _effort;
            if (body["tool_choice"] is JsonObject || body["tool_choice"]?.GetValue<string>() == "required")
                body["tool_choice"] = "auto";
        }
        foreach (var message in body["messages"]!.AsArray().OfType<JsonObject>())
        {
            if (message["role"]?.GetValue<string>() != "assistant" || message["tool_calls"] is not JsonArray calls) continue;
            message["content"] ??= JsonValue.Create("");
            if (!_thinking || message["reasoning_content"] != null) continue;
            foreach (var call in calls.OfType<JsonObject>())
            {
                if (call["id"]?.GetValue<string>() is { } id && _toolReasoning.TryGetValue(id, out var reasoning))
                {
                    message["reasoning_content"] = reasoning;
                    break;
                }
            }
        }

        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version, VersionPolicy = request.VersionPolicy,
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body.ToJsonString()))
        };
        foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var header in request.Content.Headers)
            if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        var response = await _upstream.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) return response;
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            response.Content = new CaptureContent(response.Content, new SseCapture(Remember, CaptureUsage));
        else
        {
            var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject();
            CaptureUsage(document);
            if (document["choices"] is JsonArray choices)
                foreach (var choice in choices.OfType<JsonObject>())
                    if (choice["message"] is JsonObject message && message["tool_calls"] is JsonArray calls
                        && message["reasoning_content"] is JsonValue reasoning)
                        Remember(calls.OfType<JsonObject>().Select(c => c["id"]!.GetValue<string>()), reasoning.GetValue<string>());
        }
        return response;
    }

    void Remember(IEnumerable<string> ids, string reasoning)
    {
        foreach (var id in ids) _toolReasoning[id] = reasoning;
    }

    void CaptureUsage(JsonObject document)
    {
        if (document["usage"] is not JsonObject usage) return;
        _logger?.LogInformation("DeepSeek usage prompt={PromptTokens} completion={CompletionTokens} total={TotalTokens} reasoning={ReasoningTokens}",
            usage["prompt_tokens"]?.ToJsonString(), usage["completion_tokens"]?.ToJsonString(),
            usage["total_tokens"]?.ToJsonString(), usage["completion_tokens_details"]?["reasoning_tokens"]?.ToJsonString());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _toolReasoning.Clear(); _upstream.Dispose(); }
        base.Dispose(disposing);
    }

    sealed class SseCapture
    {
        sealed class Choice
        {
            public StringBuilder Reasoning = new();
            public Dictionary<int, string> ToolIds = new();
        }
        readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        readonly Dictionary<int, Choice> _choices = new();
        readonly Action<IEnumerable<string>, string> _remember;
        readonly Action<JsonObject> _usage;
        string _pending = "";
        public SseCapture(Action<IEnumerable<string>, string> remember, Action<JsonObject> usage)
            => (_remember, _usage) = (remember, usage);
        public void Feed(ReadOnlyMemory<byte> bytes)
        {
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            var count = _decoder.GetChars(bytes.Span, chars, false);
            _pending += new string(chars, 0, count);
            int newline;
            while ((newline = _pending.IndexOf('\n')) >= 0)
            {
                var line = _pending[..newline].TrimEnd('\r');
                _pending = _pending[(newline + 1)..];
                if (!line.StartsWith("data:")) continue;
                var data = line[5..].TrimStart();
                if (data == "[DONE]")
                {
                    foreach (var choice in _choices.Values) _remember(choice.ToolIds.Values, choice.Reasoning.ToString());
                    continue;
                }
                try
                {
                    var document = JsonNode.Parse(data)!.AsObject();
                    _usage(document);
                    if (document["choices"] is not JsonArray choices) continue;
                    foreach (var choice in choices.OfType<JsonObject>())
                    {
                        var index = choice["index"]?.GetValue<int>() ?? 0;
                        if (!_choices.TryGetValue(index, out var state)) _choices[index] = state = new();
                        if (choice["delta"] is JsonObject delta)
                        {
                            if (delta["reasoning_content"] is JsonValue reasoning) state.Reasoning.Append(reasoning.GetValue<string>());
                            if (delta["tool_calls"] is JsonArray calls)
                                foreach (var call in calls.OfType<JsonObject>())
                                    if (call["id"] is JsonValue id) state.ToolIds[call["index"]?.GetValue<int>() ?? 0] = id.GetValue<string>();
                        }
                        if (choice["finish_reason"] != null) _remember(state.ToolIds.Values, state.Reasoning.ToString());
                    }
                }
                catch (JsonException) { /* SDK receives the original bytes and reports malformed events. */ }
            }
        }
    }

    sealed class CaptureContent : HttpContent
    {
        readonly HttpContent _source;
        readonly SseCapture _capture;
        public CaptureContent(HttpContent source, SseCapture capture)
        {
            (_source, _capture) = (source, capture);
            foreach (var header in source.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task<Stream> CreateContentReadStreamAsync()
            => new CaptureStream(await _source.ReadAsStreamAsync(), _capture);
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken token)
            => new CaptureStream(await _source.ReadAsStreamAsync(token), _capture);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            using var source = await CreateContentReadStreamAsync();
            await source.CopyToAsync(stream);
        }
        protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
    }

    sealed class CaptureStream : Stream
    {
        readonly Stream _source;
        readonly SseCapture _capture;
        public CaptureStream(Stream source, SseCapture capture) => (_source, _capture) = (source, capture);
        public override bool CanRead => _source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count); _capture.Feed(buffer.AsMemory(offset, read)); return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            var read = await _source.ReadAsync(buffer, offset, count, token); _capture.Feed(buffer.AsMemory(offset, read)); return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var read = await _source.ReadAsync(buffer, token); _capture.Feed(buffer[..read]); return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
    }
}
