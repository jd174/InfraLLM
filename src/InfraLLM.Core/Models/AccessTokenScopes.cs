namespace InfraLLM.Core.Models;

/// <summary>
/// Well-known scopes that can be attached to an access token to restrict what
/// an MCP client (or API caller) can do. A token with no scopes is
/// unrestricted — this preserves the behavior of tokens created before scopes
/// existed.
/// </summary>
public static class AccessTokenScopes
{
    /// <summary>Read-only visibility: host inventory, policies, audit logs, log/file reads, status checks.</summary>
    public const string Read = "mcp:read";

    /// <summary>Run arbitrary shell commands on managed hosts (still subject to command policies).</summary>
    public const string Execute = "mcp:execute";

    /// <summary>Mutate state: write files on hosts, update host notes.</summary>
    public const string Write = "mcp:write";

    public static readonly IReadOnlyList<string> All = [Read, Execute, Write];

    public static bool IsValid(string scope) => All.Contains(scope, StringComparer.Ordinal);
}
