using InfraLLM.Core.Models;

namespace InfraLLM.Core.Interfaces;

public interface IMcpServerRepository
{
    Task<McpServer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<McpServer>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<McpServer>> GetEnabledByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<McpServer>> GetAllEnabledStdioAsync(CancellationToken ct = default);
    Task<McpServer> CreateAsync(McpServer server, CancellationToken ct = default);
    Task<McpServer> UpdateAsync(McpServer server, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
