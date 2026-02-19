using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class HostRepository : IHostRepository
{
    private readonly ApplicationDbContext _db;

    public HostRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Host?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Hosts.Include(h => h.Credential).FirstOrDefaultAsync(h => h.Id == id, ct);

    public async Task<List<Host>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Hosts.Where(h => h.OrganizationId == organizationId).OrderBy(h => h.Name).ToListAsync(ct);

    public async Task<List<Host>> GetByEnvironmentAsync(Guid organizationId, string environment, CancellationToken ct = default)
        => await _db.Hosts.Where(h => h.OrganizationId == organizationId && h.Environment == environment).OrderBy(h => h.Name).ToListAsync(ct);

    public async Task<List<Guid>> GetOrganizationIdsWithHostsAsync(CancellationToken ct = default)
        => await _db.Hosts
            .AsNoTracking()
            .Select(h => h.OrganizationId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<Host> CreateAsync(Host host, CancellationToken ct = default)
    {
        host.Id = Guid.NewGuid();
        host.CreatedAt = DateTime.UtcNow;
        _db.Hosts.Add(host);
        await _db.SaveChangesAsync(ct);
        return host;
    }

    public async Task<Host> UpdateAsync(Host host, CancellationToken ct = default)
    {
        _db.Hosts.Update(host);
        await _db.SaveChangesAsync(ct);
        return host;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var host = await _db.Hosts.FindAsync([id], ct);
        if (host != null)
        {
            _db.Hosts.Remove(host);
            await _db.SaveChangesAsync(ct);
        }
    }
}
