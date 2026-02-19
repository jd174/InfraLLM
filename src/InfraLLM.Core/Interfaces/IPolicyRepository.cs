namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Policy>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<Policy> CreateAsync(Policy policy, CancellationToken ct = default);
    Task<Policy> UpdateAsync(Policy policy, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
