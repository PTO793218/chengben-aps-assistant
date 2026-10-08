namespace WePilot.Agent.Agents;

public static class ContextCompressionPolicy
{
    public static int GetCompressMessageCount(
        int totalMessageCount,
        int summarizedMessageCount,
        int triggerRounds,
        int keepRecentRounds)
    {
        var summarized = Math.Clamp(summarizedMessageCount, 0, Math.Max(0, totalMessageCount));
        var triggerMessages = Math.Max(2, triggerRounds * 2);
        var keepMessages = Math.Clamp(keepRecentRounds * 2, 2, triggerMessages);
        var unsummarized = Math.Max(0, totalMessageCount - summarized);
        return unsummarized > triggerMessages ? unsummarized - keepMessages : 0;
    }
}
