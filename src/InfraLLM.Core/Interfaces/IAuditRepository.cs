namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;
using InfraLLM.Core.Enums;

public interface IAuditRepository
{
    Task<AuditLog> CreateAsync(AuditLog log, CancellationToken ct = default);
    Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(List<AuditLog> Items, int TotalCount)> SearchAsync(
        Guid organizationId,
        string? userId = null,
        Guid? hostId = null,
        AuditEventType? eventType = null,
        DateTime? from = null,
        DateTime? to = null,
        string? commandSearch = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);
}
