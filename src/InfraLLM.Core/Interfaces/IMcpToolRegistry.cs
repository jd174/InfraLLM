using System.Text.Json.Nodes;

namespace InfraLLM.Core.Interfaces;

/// <summary>
/// Aggregates tools from all enabled MCP servers for an organization.
/// Tool names are namespaced as "mcp__{serverName}__{toolName}" to avoid collisions.
/// </summary>
public interface IMcpToolRegistry
{
    /// <summary>
    /// Returns tool definitions for all enabled MCP servers in the organization,
    /// formatted for use in Anthropic API tool definitions (JSON).
    /// </summary>
    Task<IReadOnlyList<string>> GetToolDefinitionsAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Dispatches a namespaced MCP tool call to the appropriate server.
    /// Tool name must follow the "mcp__{serverName}__{toolName}" convention.
    /// </summary>
    Task<string> DispatchToolCallAsync(string namespacedToolName, JsonObject arguments, Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the given tool name is an MCP tool (starts with "mcp__").
    /// </summary>
    bool IsMcpTool(string toolName);
}
