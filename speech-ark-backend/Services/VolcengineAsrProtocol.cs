using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WePilot.Speech.Contracts;
using WePilot.Speech.Options;

namespace WePilot.Speech.Services;

public sealed class VolcengineAsrProtocol
{
    private const byte FullClientRequest = 0x1;
    private const byte AudioOnlyRequest = 0x2;
    private const byte FullServerResponse = 0x9;
    private const byte ErrorResponse = 0xF;
    private readonly ArkSpeechOptions _options;

    public VolcengineAsrProtocol(IOptions<ArkSpeechOptions> options) => _options = options.Value;

    public byte[] CreateFullClientRequest(string requestId, string? language = null)
    {
        var body = new
        {
            user = new { uid = "wepilot-template" },
            audio = new
            {
                format = "pcm",
                codec = "raw",
                rate = 16000,
                bits = 16,
                channel = 1,
                language = string.IsNullOrWhiteSpace(language) ? _options.AsrLanguage : language
            },
            request = new
            {
                reqid = requestId,
                model_name = "bigmodel",
                enable_nonstream = _options.AsrEnableNonstream,
                enable_itn = true,
                enable_punc = true,
                enable_ddc = true,
                show_utterances = true,
                result_type = "full",
                end_window_size = _options.AsrEndWindowSize
            }
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(body);
        var compressed = Gzip(json);
        return BuildPacket(FullClientRequest, 0, 1, 1, compressed);
    }

    public byte[] CreateAudioPacket(ReadOnlySpan<byte> pcm, bool isLast) =>
        BuildPacket(AudioOnlyRequest, isLast ? (byte)2 : (byte)0, 0, 0, pcm);

    public IReadOnlyList<BrowserAsrEvent> ParseServerPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 8)
        {
            return new[] { new BrowserAsrEvent("error", Code: "ARK_PROTOCOL", Message: "Response packet is too short.") };
        }

        var headerSize = (packet[0] & 0x0F) * 4;
        var messageType = (byte)(packet[1] >> 4);
        var flags = (byte)(packet[1] & 0x0F);
        var serialization = (byte)(packet[2] >> 4);
        var compression = (byte)(packet[2] & 0x0F);
        var offset = headerSize;

        if (messageType == ErrorResponse)
        {
            if (packet.Length < offset + 8) return new[] { new BrowserAsrEvent("error", Code: "ARK_PROTOCOL") };
            var code = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(offset, 4));
            offset += 4;
            var size = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(offset, 4));
            offset += 4;
            var message = size > 0 && packet.Length >= offset + size
                ? System.Text.Encoding.UTF8.GetString(packet.Slice(offset, size))
                : "Unknown Volcengine ASR error.";
            return new[] { new BrowserAsrEvent("error", Code: code.ToString(), Message: message) };
        }

        if (messageType != FullServerResponse)
        {
            return Array.Empty<BrowserAsrEvent>();
        }

        if ((flags & 1) != 0) offset += 4;
        if (packet.Length < offset + 4) return Array.Empty<BrowserAsrEvent>();
        var payloadSize = BinaryPrimitives.ReadInt32BigEndian(packet.Slice(offset, 4));
        offset += 4;
        if (payloadSize <= 0 || packet.Length < offset + payloadSize) return Array.Empty<BrowserAsrEvent>();

        var payload = packet.Slice(offset, payloadSize).ToArray();
        if (compression == 1) payload = Gunzip(payload);
        if (serialization != 1) return Array.Empty<BrowserAsrEvent>();

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("result", out var result)) return Array.Empty<BrowserAsrEvent>();
        if (result.ValueKind == JsonValueKind.Array)
        {
            result = result.GetArrayLength() > 0 ? result[0] : default;
        }
        if (result.ValueKind != JsonValueKind.Object) return Array.Empty<BrowserAsrEvent>();

        var text = result.TryGetProperty("text", out var textNode) ? textNode.GetString() ?? string.Empty : string.Empty;
        var events = new List<BrowserAsrEvent>();
        var definite = false;

        if (result.TryGetProperty("utterances", out var utterances) && utterances.ValueKind == JsonValueKind.Array)
        {
            foreach (var utterance in utterances.EnumerateArray())
            {
                if (!utterance.TryGetProperty("definite", out var definiteNode) || !definiteNode.GetBoolean()) continue;
                definite = true;
                events.Add(new BrowserAsrEvent(
                    "final",
                    utterance.TryGetProperty("text", out var utteranceText) ? utteranceText.GetString() : text,
                    true,
                    ReadInt(utterance, "start_time"),
                    ReadInt(utterance, "end_time")));
            }
        }

        if (!definite && !string.IsNullOrWhiteSpace(text)) events.Add(new BrowserAsrEvent("partial", text));
        return events;
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static byte[] BuildPacket(byte type, byte flags, byte serialization, byte compression, ReadOnlySpan<byte> payload)
    {
        var packet = new byte[8 + payload.Length];
        packet[0] = 0x11;
        packet[1] = (byte)((type << 4) | flags);
        packet[2] = (byte)((serialization << 4) | compression);
        packet[3] = 0;
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4, 4), payload.Length);
        payload.CopyTo(packet.AsSpan(8));
        return packet;
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
