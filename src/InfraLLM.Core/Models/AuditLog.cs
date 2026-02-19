using InfraLLM.Core.Enums;

namespace InfraLLM.Core.Models;

public class AuditLog
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public Guid? HostId { get; set; }
    public string? HostName { get; set; }
    public AuditEventType EventType { get; set; }
    public string? Command { get; set; }
    public bool? WasAllowed { get; set; }
    public string? DenialReason { get; set; }
    public Guid? ExecutionId { get; set; }
    public string? LlmReasoning { get; set; }
    public DateTime Timestamp { get; set; }
    public string? MetadataJson { get; set; }

    public Organization Organization { get; set; } = null!;
    public CommandExecution? Execution { get; set; }
}
