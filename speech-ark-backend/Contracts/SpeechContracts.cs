namespace WePilot.Speech.Contracts;

public sealed record TtsStreamRequest(
    string Text,
    string? Speaker = null,
    string? Format = null,
    int? SampleRate = null);

public sealed record BrowserAsrEvent(
    string Type,
    string? Text = null,
    bool Definite = false,
    int? StartTime = null,
    int? EndTime = null,
    string? Code = null,
    string? Message = null);
