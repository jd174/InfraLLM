using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class HostNoteRepository : IHostNoteRepository
{
    private readonly ApplicationDbContext _db;

    public HostNoteRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<HostNote>> GetByHostIdsAsync(Guid organizationId, List<Guid> hostIds, CancellationToken ct = default)
    {
        if (hostIds.Count == 0) return [];
        return await _db.HostNotes
            .AsNoTracking()
            .Where(n => n.OrganizationId == organizationId && hostIds.Contains(n.HostId))
            .ToListAsync(ct);
    }

    public async Task<HostNote?> GetByHostIdAsync(Guid organizationId, Guid hostId, CancellationToken ct = default)
        => await _db.HostNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.OrganizationId == organizationId && n.HostId == hostId, ct);

    public async Task<HostNote> UpsertAsync(HostNote note, CancellationToken ct = default)
    {
        var existing = await _db.HostNotes
            .FirstOrDefaultAsync(n => n.OrganizationId == note.OrganizationId && n.HostId == note.HostId, ct);

        if (existing == null)
        {
            note.Id = Guid.NewGuid();
            note.CreatedAt = DateTime.UtcNow;
            note.UpdatedAt = DateTime.UtcNow;
            _db.HostNotes.Add(note);
            await _db.SaveChangesAsync(ct);
            return note;
        }

        existing.Content = note.Content;
        existing.UpdatedByUserId = note.UpdatedByUserId;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return existing;
    }
}
