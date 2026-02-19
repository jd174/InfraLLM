using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class CredentialRepository : ICredentialRepository
{
    private readonly ApplicationDbContext _db;

    public CredentialRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Credential?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Credentials.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<List<Credential>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Credentials
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task<Credential> CreateAsync(Credential credential, CancellationToken ct = default)
    {
        credential.Id = Guid.NewGuid();
        credential.CreatedAt = DateTime.UtcNow;
        _db.Credentials.Add(credential);
        await _db.SaveChangesAsync(ct);
        return credential;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var cred = await _db.Credentials.FindAsync([id], ct);
        if (cred != null)
        {
            _db.Credentials.Remove(cred);
            await _db.SaveChangesAsync(ct);
        }
    }
}
