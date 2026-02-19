using InfraLLM.Core.Interfaces;
using InfraLLM.Infrastructure.Services.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InfraLLM.Api.Services;

/// <summary>
/// Background hosted service that pre-warms all enabled stdio MCP server processes at
/// application startup.
///
/// Stdio servers (e.g. those run via uvx/npx) can take a long time to start on first
/// use — uvx may need to download the package before the process is ready.  By starting
/// them eagerly in the background we ensure the process is already running (and
/// fully initialised) by the time the first user request arrives, so there is no
/// cold-start timeout.
///
/// This service does NOT block app startup: it returns immediately from StartAsync and
/// fires the warmup tasks in the background, logging any failures without crashing.
/// </summary>
public sealed class StdioMcpWarmupService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly StdioMcpClientCache _cache;
    private readonly ILogger<StdioMcpWarmupService> _logger;

    public StdioMcpWarmupService(
        IServiceScopeFactory scopeFactory,
        StdioMcpClientCache cache,
        ILogger<StdioMcpWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire and forget — do not await so startup is not delayed
        _ = Task.Run(() => WarmupAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task WarmupAsync(CancellationToken ct)
    {
        // Small delay to let the DB migration in Program.cs finish first
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        IReadOnlyList<InfraLLM.Core.Models.McpServer> servers;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMcpServerRepository>();
            servers = await repo.GetAllEnabledStdioAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StdioMcpWarmupService: failed to load enabled stdio servers from DB");
            return;
        }

        if (servers.Count == 0)
        {
            _logger.LogInformation("StdioMcpWarmupService: no enabled stdio MCP servers to pre-warm");
            return;
        }

        _logger.LogInformation(
            "StdioMcpWarmupService: pre-warming {Count} stdio MCP server(s): {Names}",
            servers.Count,
            string.Join(", ", servers.Select(s => s.Name)));

        // Warm all servers concurrently — each may take minutes on cold start
        var tasks = servers.Select(server => WarmOneAsync(server, ct));
        await Task.WhenAll(tasks);

        _logger.LogInformation("StdioMcpWarmupService: pre-warm complete");
    }

    private async Task WarmOneAsync(InfraLLM.Core.Models.McpServer server, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "StdioMcpWarmupService: starting '{Name}' in background...", server.Name);

            var client = await _cache.GetOrCreateAsync(server, ct);

            // Run the MCP initialize handshake now so it's complete before the first
            // user request arrives.  ListToolsAsync calls EnsureInitializedAsync internally.
            // We use a generous 5-minute timeout so slow uvx cold-starts (Python bytecode
            // compilation on first run) have time to finish without being cancelled by an
            // upstream HTTP request timeout.
            using var warmupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            warmupCts.CancelAfter(TimeSpan.FromMinutes(5));

            var tools = await client.ListToolsAsync(warmupCts.Token);

            _logger.LogInformation(
                "StdioMcpWarmupService: '{Name}' is ready — {Count} tool(s) discovered",
                server.Name, tools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "StdioMcpWarmupService: failed to pre-warm '{Name}' — it will be started on first use instead",
                server.Name);
        }
    }
}
