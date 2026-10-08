namespace WePilot.Agent.Tools.Results;

public sealed class ToolExecutionContext
{
    public List<ToolCallRecord> ToolCalls { get; } = new();

    public List<UiBlock> UiPayload { get; } = new();
}

public sealed class ToolCallRecord
{
    public string ToolName { get; set; } = "";
    public object? Arguments { get; set; }

    public bool Success { get; set; }

    public string Summary { get; set; } = "";

    public int ElapsedMs { get; set; }
}
