using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class McpServerRepository : IMcpServerRepository
{
    private readonly ApplicationDbContext _db;

    public McpServerRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<McpServer?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.McpServers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<McpServer>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.McpServers
            .AsNoTracking()
            .Where(s => s.OrganizationId == organizationId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<McpServer>> GetEnabledByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await _db.McpServers
            .AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.IsEnabled)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<McpServer>> GetAllEnabledStdioAsync(CancellationToken ct = default)
        => await _db.McpServers
            .AsNoTracking()
            .Where(s => s.IsEnabled && s.TransportType == InfraLLM.Core.Enums.McpTransportType.Stdio)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<McpServer> CreateAsync(McpServer server, CancellationToken ct = default)
    {
        server.Id = Guid.NewGuid();
        server.CreatedAt = DateTime.UtcNow;
        _db.McpServers.Add(server);
        await _db.SaveChangesAsync(ct);
        return server;
    }

    public async Task<McpServer> UpdateAsync(McpServer server, CancellationToken ct = default)
    {
        _db.McpServers.Update(server);
        await _db.SaveChangesAsync(ct);
        return server;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var server = await _db.McpServers.FindAsync([id], ct);
        if (server != null)
        {
            _db.McpServers.Remove(server);
            await _db.SaveChangesAsync(ct);
        }
    }
}
