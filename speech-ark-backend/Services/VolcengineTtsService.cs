using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WePilot.Speech.Contracts;
using WePilot.Speech.Options;

namespace WePilot.Speech.Services;

public sealed class VolcengineTtsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ArkSpeechOptions _options;

    public VolcengineTtsService(IHttpClientFactory httpClientFactory, IOptions<ArkSpeechOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task StreamAsync(TtsStreamRequest input, HttpResponse target, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("Text is required.");
        var speaker = string.IsNullOrWhiteSpace(input.Speaker) ? _options.TtsSpeaker : input.Speaker;
        if (string.IsNullOrWhiteSpace(speaker)) throw new InvalidOperationException("ArkSpeech:TtsSpeaker is required.");
        var format = string.IsNullOrWhiteSpace(input.Format) ? _options.TtsFormat : input.Format;
        var sampleRate = input.SampleRate ?? _options.TtsSampleRate;

        var body = new
        {
            req_params = new
            {
                text = input.Text,
                speaker,
                audio_params = new { format, sample_rate = sampleRate }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TtsEndpoint);
        request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Api-Resource-Id", _options.TtsResourceId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var client = _httpClientFactory.CreateClient(nameof(VolcengineTtsService));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        target.StatusCode = StatusCodes.Status200OK;
        target.ContentType = format?.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "ogg_opus" or "ogg" => "audio/ogg",
            _ => "audio/pcm"
        };
        target.Headers.CacheControl = "no-store";
        target.Headers["X-Audio-Sample-Rate"] = sampleRate.ToString();
        target.Headers["X-Audio-Format"] = format;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.TryGetProperty("code", out var codeNode) && codeNode.GetInt32() is not (0 or 20000000))
            {
                throw new InvalidOperationException($"Volcengine TTS failed: {line}");
            }
            if (!root.TryGetProperty("data", out var dataNode) || dataNode.ValueKind != JsonValueKind.String) continue;
            var encoded = dataNode.GetString();
            if (string.IsNullOrWhiteSpace(encoded)) continue;
            var audio = Convert.FromBase64String(encoded);
            await target.Body.WriteAsync(audio, cancellationToken);
            await target.Body.FlushAsync(cancellationToken);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("ArkSpeech:ApiKey is required.");
    }
}
