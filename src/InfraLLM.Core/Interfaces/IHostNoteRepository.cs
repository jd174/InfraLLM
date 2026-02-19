using InfraLLM.Core.Models;

namespace InfraLLM.Core.Interfaces;

public interface IHostNoteRepository
{
    Task<List<HostNote>> GetByHostIdsAsync(Guid organizationId, List<Guid> hostIds, CancellationToken ct = default);
    Task<HostNote?> GetByHostIdAsync(Guid organizationId, Guid hostId, CancellationToken ct = default);
    Task<HostNote> UpsertAsync(HostNote note, CancellationToken ct = default);
}
