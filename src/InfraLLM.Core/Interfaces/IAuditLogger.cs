namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Enums;
using InfraLLM.Core.Models;

public interface IAuditLogger
{
    Task LogCommandExecutedAsync(Guid organizationId, string userId, Guid? sessionId, Guid hostId, string hostName, string command, CommandResult result, string? llmReasoning = null, CancellationToken ct = default);
    Task LogCommandDeniedAsync(Guid organizationId, string userId, Guid? sessionId, Guid hostId, string hostName, string command, string reason, CancellationToken ct = default);
    Task LogEventAsync(Guid organizationId, string userId, AuditEventType eventType, string? details = null, CancellationToken ct = default);
}
