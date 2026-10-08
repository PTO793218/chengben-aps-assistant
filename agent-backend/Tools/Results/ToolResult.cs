using System.Text.Json;

namespace WePilot.Agent.Tools.Results;

public sealed class ToolResult
{
    public bool Success { get; set; }

    public string Summary { get; set; } = "";

    public object? AgentContext { get; set; }

    public List<UiBlock> UiPayload { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public string ToModelJson()
    {
        return JsonSerializer.Serialize(new
        {
            success = Success,
            summary = Summary,
            agentContext = AgentContext,
            errorMessage = ErrorMessage
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static ToolResult FromModelJson(string json)
    {
        return JsonSerializer.Deserialize<ToolResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new ToolResult
            {
                Success = false,
                Summary = "Tool result deserialize failed.",
                ErrorMessage = "Empty tool result."
            };
    }
}

public sealed class UiBlock
{
    public string Type { get; set; } = "chart";

    public string Title { get; set; } = "";

    public object Spec { get; set; } = new();
}
