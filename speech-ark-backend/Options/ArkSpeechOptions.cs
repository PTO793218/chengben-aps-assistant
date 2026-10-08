namespace WePilot.Speech.Options;

public sealed class ArkSpeechOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string AsrEndpoint { get; init; } = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
    public string AsrResourceId { get; init; } = "volc.seedasr.sauc.duration";
    public string TtsEndpoint { get; init; } = "https://openspeech.bytedance.com/api/v3/tts/unidirectional";
    public string TtsResourceId { get; init; } = "seed-tts-2.0";
    public string TtsSpeaker { get; init; } = string.Empty;
    public int AsrEndWindowSize { get; init; } = 800;
    public bool AsrEnableNonstream { get; init; } = true;
    public string AsrLanguage { get; init; } = "zh-CN";
    public int TtsSampleRate { get; init; } = 24000;
    public string TtsFormat { get; init; } = "pcm";
}
