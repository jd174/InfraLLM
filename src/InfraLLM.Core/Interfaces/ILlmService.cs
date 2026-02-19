namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public class LlmResponse
{
    public string MessageId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<ToolCall> ToolCalls { get; set; } = [];
    public int TokensUsed { get; set; }
    public decimal Cost { get; set; }
}

public class ToolCall
{
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = [];
}

public interface ILlmService
{
    Task<LlmResponse> SendMessageAsync(
        string userId,
        string sessionId,
        Guid organizationId,
        string message,
        List<Host> availableHosts,
        List<Policy> policies,
        List<Message> conversationHistory,
        string? customSystemPrompt,
        string? personalizationPrompt,
        string? modelOverride,
        CancellationToken ct = default);

    Task<LlmResponse> SendMessageStreamAsync(
        string userId,
        string sessionId,
        Guid organizationId,
        string message,
        List<Host> availableHosts,
        List<Policy> policies,
        List<Message> conversationHistory,
        string? customSystemPrompt,
        string? personalizationPrompt,
        string? modelOverride,
        Func<string, Task> onTextDelta,
        Func<string, Task>? onStatusUpdate = null,
        CancellationToken ct = default);

    Task<string> BuildSystemPromptAsync(
        string userId,
        List<Host> availableHosts,
        List<Policy> policies,
        string? customSystemPrompt,
        string? personalizationPrompt);

    Task<string?> GenerateSessionTitleAsync(
        string userId,
        List<Message> conversationHistory,
        CancellationToken ct = default);
}
