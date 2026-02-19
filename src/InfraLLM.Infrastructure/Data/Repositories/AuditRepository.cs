using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Core.Enums;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly ApplicationDbContext _db;

    public AuditRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AuditLog> CreateAsync(AuditLog log, CancellationToken ct = default)
    {
        log.Id = Guid.NewGuid();
        log.Timestamp = DateTime.UtcNow;
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync(ct);
        return log;
    }

    public async Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.AuditLogs.Include(a => a.Execution).FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<(List<AuditLog> Items, int TotalCount)> SearchAsync(
        Guid organizationId,
        string? userId = null,
        Guid? hostId = null,
        AuditEventType? eventType = null,
        DateTime? from = null,
        DateTime? to = null,
        string? commandSearch = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = _db.AuditLogs.Where(a => a.OrganizationId == organizationId);

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(a => a.UserId == userId);
        if (hostId.HasValue)
            query = query.Where(a => a.HostId == hostId.Value);
        if (eventType.HasValue)
            query = query.Where(a => a.EventType == eventType.Value);
        if (from.HasValue)
            query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(a => a.Timestamp <= to.Value);
        if (!string.IsNullOrEmpty(commandSearch))
            query = query.Where(a => a.Command != null && a.Command.Contains(commandSearch));

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
