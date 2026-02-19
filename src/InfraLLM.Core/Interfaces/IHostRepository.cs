namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public interface IHostRepository
{
    Task<Host?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Host>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<List<Host>> GetByEnvironmentAsync(Guid organizationId, string environment, CancellationToken ct = default);
    Task<List<Guid>> GetOrganizationIdsWithHostsAsync(CancellationToken ct = default);
    Task<Host> CreateAsync(Host host, CancellationToken ct = default);
    Task<Host> UpdateAsync(Host host, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
