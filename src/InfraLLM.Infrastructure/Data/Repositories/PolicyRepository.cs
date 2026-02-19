using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class PolicyRepository : IPolicyRepository
{
    private readonly ApplicationDbContext _db;

    public PolicyRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Policies.Include(p => p.UserPolicies).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<List<Policy>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.Policies.Where(p => p.OrganizationId == organizationId).OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<Policy> CreateAsync(Policy policy, CancellationToken ct = default)
    {
        policy.Id = Guid.NewGuid();
        policy.CreatedAt = DateTime.UtcNow;
        _db.Policies.Add(policy);
        await _db.SaveChangesAsync(ct);
        return policy;
    }

    public async Task<Policy> UpdateAsync(Policy policy, CancellationToken ct = default)
    {
        _db.Policies.Update(policy);
        await _db.SaveChangesAsync(ct);
        return policy;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await _db.Policies.FindAsync([id], ct);
        if (policy != null)
        {
            _db.Policies.Remove(policy);
            await _db.SaveChangesAsync(ct);
        }
    }
}
