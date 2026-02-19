namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public interface ICredentialRepository
{
    Task<Credential?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Credential>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<Credential> CreateAsync(Credential credential, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
