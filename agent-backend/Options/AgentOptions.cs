namespace WePilot.Agent.Options;

public sealed class AgentOptions
{
    public string Name { get; set; } = "WePilot";

    public string SystemPrompt { get; set; } = "";

    public int HistoryMaxRounds { get; set; } = 6;

    public int SummaryTriggerRounds { get; set; } = 8;

    public int SummaryKeepRecentRounds { get; set; } = 4;

    public int SummaryMaxOutputTokens { get; set; } = 400;
}
