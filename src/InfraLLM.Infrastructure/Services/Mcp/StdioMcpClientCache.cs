using System.Collections.Concurrent;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using Microsoft.Extensions.Logging;

namespace InfraLLM.Infrastructure.Services.Mcp;

/// <summary>
/// Singleton cache that keeps stdio MCP server processes alive across requests.
///
/// Stdio processes are expensive to spawn (especially uvx which downloads packages on first run),
/// so we keep them running and reuse the same <see cref="StdioMcpClient"/> instance.
/// Call <see cref="InvalidateAsync"/> when a server is updated or deleted so the old process
/// is cleaned up and a fresh one is started on next use.
///
/// Also maintains a per-server rolling log buffer (last 200 entries) so the UI can display
/// live process output without needing a dedicated log sink.
/// </summary>
public sealed class StdioMcpClientCache : IAsyncDisposable
{
    private readonly IMcpClientFactory _factory;
    private readonly ILogger<StdioMcpClientCache> _logger;

    // Key: McpServer.Id — value: lazily-initialized running client
    private readonly ConcurrentDictionary<Guid, Lazy<Task<StdioMcpClient>>> _clients = new();

    // Per-server rolling log ring buffers (capped at MaxLogEntries)
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<McpLogEntry>> _logs = new();
    private const int MaxLogEntries = 200;

    public StdioMcpClientCache(IMcpClientFactory factory, ILogger<StdioMcpClientCache> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>
    /// Returns the cached <see cref="StdioMcpClient"/> for the given server, starting a new
    /// process if one isn't already running. Thread-safe: concurrent callers for the same server
    /// ID will share a single Lazy so the process is only started once.
    /// </summary>
    public async Task<StdioMcpClient> GetOrCreateAsync(McpServer server, CancellationToken ct = default)
    {
        var lazy = _clients.GetOrAdd(server.Id, _ => new Lazy<Task<StdioMcpClient>>(
            () => CreateClientAsync(server),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var client = await lazy.Value;

            // If the process has exited (crash, OOM, etc.) remove and recreate
            if (client.HasProcessExited)
            {
                _logger.LogWarning(
                    "stdio MCP server '{Name}' process has exited unexpectedly — restarting",
                    server.Name);

                AppendLog(server.Id, "warn", "Process exited unexpectedly — restarting…");
                await InvalidateAsync(server.Id);
                return await GetOrCreateAsync(server, ct);
            }

            return client;
        }
        catch
        {
            // Remove the failed Lazy so the next caller gets a fresh attempt
            _clients.TryRemove(server.Id, out _);
            throw;
        }
    }

    /// <summary>
    /// Returns the last <paramref name="count"/> log entries for the given server, newest last.
    /// Returns an empty list if the server has never been started.
    /// </summary>
    public IReadOnlyList<McpLogEntry> GetLogs(Guid serverId, int count = 100)
    {
        if (!_logs.TryGetValue(serverId, out var queue))
            return [];

        // Snapshot the queue (ConcurrentQueue is safe to enumerate)
        var entries = queue.ToArray();
        return count >= entries.Length
            ? entries
            : entries[^count..]; // last N
    }

    /// <summary>
    /// Disposes the cached client for the given server ID and removes it from the cache.
    /// Call this when a server is updated or deleted.
    /// </summary>
    public async Task InvalidateAsync(Guid serverId)
    {
        if (_clients.TryRemove(serverId, out var lazy))
        {
            try
            {
                // Only dispose if the task completed (i.e. the process actually started)
                if (lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
                    await lazy.Value.Result.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing cached stdio MCP client for server {Id}", serverId);
            }
        }
    }

    /// <summary>
    /// Disposes all cached clients. Called on application shutdown.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var ids = _clients.Keys.ToArray();
        await Task.WhenAll(ids.Select(id => InvalidateAsync(id)));
    }

    private async Task<StdioMcpClient> CreateClientAsync(McpServer server)
    {
        _logger.LogInformation(
            "StdioMcpClientCache: starting persistent process for server '{Name}' ({Id})",
            server.Name, server.Id);

        AppendLog(server.Id, "info", $"Starting process: {server.Command} {server.Arguments}".TrimEnd());

        // Ensure the log queue exists before the client starts emitting
        _logs.GetOrAdd(server.Id, _ => new ConcurrentQueue<McpLogEntry>());

        // Pass the log sink so the client feeds stderr/lifecycle events into our buffer
        var sink = (McpLogEntry entry) => AppendLog(server.Id, entry.Level, entry.Message);
        var client = (StdioMcpClient)await ((McpClientFactory)_factory).CreateAsync(server, sink);
        return client;
    }

    private void AppendLog(Guid serverId, string level, string message)
    {
        var queue = _logs.GetOrAdd(serverId, _ => new ConcurrentQueue<McpLogEntry>());
        queue.Enqueue(new McpLogEntry(DateTime.UtcNow, level, message));

        // Trim to cap — dequeue oldest entries when over limit
        while (queue.Count > MaxLogEntries)
            queue.TryDequeue(out _);
    }
}
