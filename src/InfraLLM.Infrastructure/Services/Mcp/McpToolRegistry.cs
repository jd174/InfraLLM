using System.Text.Json;
using System.Text.Json.Nodes;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace InfraLLM.Infrastructure.Services.Mcp;

/// <summary>
/// Aggregates tools from all enabled MCP servers for an organization.
/// Tool names are namespaced as "mcp__{serverName}__{toolName}" to prevent collisions.
///
/// Stdio servers use the <see cref="StdioMcpClientCache"/> so their processes stay alive across
/// requests (uvx/npx are expensive to cold-start). HTTP servers get a fresh client per call.
///
/// Built-in SSH tools (execute_command, read_file, check_service_status) are NOT managed here —
/// they remain in AnthropicLlmService. This registry only handles MCP-sourced tools.
/// </summary>
public class McpToolRegistry : IMcpToolRegistry
{
    private const string McpPrefix = "mcp__";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IMcpServerRepository _serverRepo;
    private readonly IMcpClientFactory _clientFactory;
    private readonly StdioMcpClientCache _stdioCache;
    private readonly IMemoryCache _cache;
    private readonly ILogger<McpToolRegistry> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public McpToolRegistry(
        IMcpServerRepository serverRepo,
        IMcpClientFactory clientFactory,
        StdioMcpClientCache stdioCache,
        IMemoryCache cache,
        ILogger<McpToolRegistry> logger)
    {
        _serverRepo = serverRepo;
        _clientFactory = clientFactory;
        _stdioCache = stdioCache;
        _cache = cache;
        _logger = logger;
    }

    public bool IsMcpTool(string toolName)
        => toolName.StartsWith(McpPrefix, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<string>> GetToolDefinitionsAsync(Guid organizationId, CancellationToken ct = default)
    {
        var cacheKey = $"mcp_tools_{organizationId}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached != null)
            return cached;

        var definitions = new List<string>();
        var servers = await _serverRepo.GetEnabledByOrganizationAsync(organizationId, ct);

        foreach (var server in servers)
        {
            try
            {
                await using var client = await GetClientAsync(server, ct);
                var tools = await client.ListToolsAsync(ct);

                foreach (var tool in tools)
                {
                    var namespacedName = BuildNamespacedName(server.Name, tool.Name);

                    // Build Anthropic-compatible tool definition JSON
                    var toolDef = new JsonObject
                    {
                        ["name"] = namespacedName,
                        ["description"] = $"[{server.Name}] {tool.Description}",
                        ["input_schema"] = tool.InputSchema.DeepClone()
                    };

                    definitions.Add(toolDef.ToJsonString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to discover tools from MCP server '{Name}' ({Id})",
                    server.Name, server.Id);
                // Continue with remaining servers — one failure shouldn't block others
            }
        }

        _cache.Set(cacheKey, (IReadOnlyList<string>)definitions, CacheTtl);
        _logger.LogInformation("Discovered {Count} MCP tools for org {OrgId} from {ServerCount} server(s)",
            definitions.Count, organizationId, servers.Count);

        return definitions;
    }

    public async Task<string> DispatchToolCallAsync(
        string namespacedToolName,
        JsonObject arguments,
        Guid organizationId,
        CancellationToken ct = default)
    {
        if (!IsMcpTool(namespacedToolName))
            throw new ArgumentException($"'{namespacedToolName}' is not a namespaced MCP tool name.", nameof(namespacedToolName));

        var (serverName, toolName) = ParseNamespacedName(namespacedToolName);

        if (string.IsNullOrEmpty(serverName) || string.IsNullOrEmpty(toolName))
            return $"Error: Invalid MCP tool name format '{namespacedToolName}'. Expected 'mcp__serverName__toolName'.";

        var servers = await _serverRepo.GetEnabledByOrganizationAsync(organizationId, ct);
        var server = servers.FirstOrDefault(s =>
            string.Equals(NormalizeName(s.Name), serverName, StringComparison.OrdinalIgnoreCase));

        if (server == null)
        {
            _logger.LogWarning("No enabled MCP server found matching '{ServerName}' for org {OrgId}",
                serverName, organizationId);
            return $"Error: MCP server '{serverName}' not found or is disabled.";
        }

        try
        {
            await using var client = await GetClientAsync(server, ct);
            _logger.LogInformation("Dispatching MCP tool call: {Tool} on server '{Server}'",
                toolName, server.Name);

            var result = await client.CallToolAsync(toolName, arguments, ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool call failed: {Tool} on server '{Server}'", toolName, server.Name);
            return $"Error calling MCP tool '{toolName}' on server '{server.Name}': {ex.Message}";
        }
    }

    /// <summary>
    /// Returns an <see cref="IMcpClient"/> for the server.
    /// Stdio servers are retrieved from the persistent cache (process stays alive).
    /// HTTP servers get a fresh client per call (stateless).
    ///
    /// The returned wrapper's DisposeAsync is a no-op for cached stdio clients so the
    /// caller can still use <c>await using</c> without killing the process.
    /// </summary>
    private async Task<IMcpClient> GetClientAsync(McpServer server, CancellationToken ct)
    {
        if (server.TransportType == McpTransportType.Stdio)
        {
            var cached = await _stdioCache.GetOrCreateAsync(server, ct);
            // Wrap in a non-disposing shell so "await using" in the caller doesn't kill the process
            return new NonDisposingMcpClientWrapper(cached);
        }

        // HTTP: fresh client per call, dispose when done
        return await _clientFactory.CreateAsync(server, ct);
    }

    /// <summary>
    /// Thin wrapper that forwards all calls to the inner client but suppresses DisposeAsync.
    /// Used so the McpToolRegistry can use "await using" syntax safely with cached stdio clients.
    /// </summary>
    private sealed class NonDisposingMcpClientWrapper(IMcpClient inner) : IMcpClient
    {
        public Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
            => inner.ListToolsAsync(ct);

        public Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken ct = default)
            => inner.CallToolAsync(toolName, arguments, ct);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask; // cache owns the real lifetime
    }

    /// <summary>
    /// Builds a namespaced tool name: "mcp__{normalizedServerName}__{toolName}"
    /// </summary>
    private static string BuildNamespacedName(string serverName, string toolName)
        => $"{McpPrefix}{NormalizeName(serverName)}__{toolName}";

    /// <summary>
    /// Parses "mcp__{serverName}__{toolName}" into its components.
    /// Returns (serverName, toolName) or ("", "") on failure.
    /// </summary>
    private static (string serverName, string toolName) ParseNamespacedName(string namespacedName)
    {
        // Strip "mcp__" prefix
        var withoutPrefix = namespacedName[McpPrefix.Length..];

        // Find the second "__" separator
        var separatorIndex = withoutPrefix.IndexOf("__", StringComparison.Ordinal);
        if (separatorIndex < 0)
            return ("", "");

        var serverPart = withoutPrefix[..separatorIndex];
        var toolPart = withoutPrefix[(separatorIndex + 2)..];

        return (serverPart, toolPart);
    }

    /// <summary>
    /// Normalizes a server name for use in tool name namespacing.
    /// Replaces spaces and special chars with underscores, lowercases.
    /// </summary>
    private static string NormalizeName(string name)
        => System.Text.RegularExpressions.Regex
            .Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_")
            .Trim('_');
}
