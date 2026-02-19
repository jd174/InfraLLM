using System.Text.Json;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Infrastructure.Services;

public class AuditLogService : IAuditLogger
{
    private readonly ApplicationDbContext _db;

    public AuditLogService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task LogCommandExecutedAsync(
        Guid organizationId, string userId, Guid? sessionId, Guid hostId,
        string hostName, string command, CommandResult result,
        string? llmReasoning = null, CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            SessionId = sessionId,
            HostId = hostId,
            HostName = hostName,
            EventType = AuditEventType.CommandExecuted,
            Command = command,
            WasAllowed = true,
            ExecutionId = result.ExecutionId,
            LlmReasoning = llmReasoning,
            Timestamp = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task LogCommandDeniedAsync(
        Guid organizationId, string userId, Guid? sessionId, Guid hostId,
        string hostName, string command, string reason,
        CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            SessionId = sessionId,
            HostId = hostId,
            HostName = hostName,
            EventType = AuditEventType.CommandDenied,
            Command = command,
            WasAllowed = false,
            DenialReason = reason,
            Timestamp = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task LogEventAsync(
        Guid organizationId, string userId, AuditEventType eventType,
        string? details = null, CancellationToken ct = default)
    {
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = userId,
            EventType = eventType,
            MetadataJson = details != null ? JsonSerializer.Serialize(new { message = details }) : null,
            Timestamp = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }
}
