namespace InfraLLM.Core.Interfaces;

public interface ISshConnectionPool
{
    Task<object> GetConnectionAsync(Guid hostId, CancellationToken ct = default);
    Task ReleaseConnectionAsync(Guid hostId, object client);
    Task<bool> TestConnectionAsync(Guid hostId, CancellationToken ct = default);
    Task InvalidateHostAsync(Guid hostId);
}
