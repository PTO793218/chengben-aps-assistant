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
