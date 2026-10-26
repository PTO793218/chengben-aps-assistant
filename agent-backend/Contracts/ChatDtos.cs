using System.ComponentModel.DataAnnotations;
namespace WePilot.Agent.Contracts;

public sealed class ChatStreamRequest
{
    public string? ConversationId { get; set; }

    [Required, StringLength(20000)]
    public string Message { get; set; } = "";

    [Required, MaxLength(200)]
    public List<ChatMessageDto> History { get; set; } = new();

    [StringLength(20000)]
    public string? Summary { get; set; }

    public int SummarizedMessageCount { get; set; }

    public WePilot.Agent.Tools.DemoPlanState? DemoPlan { get; set; }
    public ConfirmedDemoAction? DemoAction { get; set; }
}

// Confirmed by the local form. Never generated or amended by model tool arguments.
public sealed class ConfirmedDemoAction
{
    [StringLength(30)] public string Action { get; set; } = "";
    [StringLength(50)] public string OrderId { get; set; } = "";
    public int Position { get; set; } = 1;
    public int Quantity { get; set; } = 12500;
    [StringLength(30)] public string Scenario { get; set; } = "none";
    [StringLength(50)] public string Version { get; set; } = WePilot.Agent.Tools.InteractiveDemoTools.Version;
}

public sealed class ChatMessageDto
{
    [Required, StringLength(20)]
    public string Role { get; set; } = "user";

    [Required, StringLength(20000)]
    public string Content { get; set; } = "";
}

public sealed class ErrorResponse
{
    public string Code { get; set; } = "INTERNAL_ERROR";

    public string Message { get; set; } = "";

    public string RequestId { get; set; } = "";
}
