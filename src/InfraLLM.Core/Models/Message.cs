namespace InfraLLM.Core.Models;

public class Message
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty; // user, assistant
    public string Content { get; set; } = string.Empty;
    public string? ToolCallsJson { get; set; }
    public int? TokensUsed { get; set; }
    public DateTime CreatedAt { get; set; }

    public Session Session { get; set; } = null!;
}
