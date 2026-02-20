using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Api.Controllers;

/// <summary>
/// Exposes InfraLLM itself as an MCP (Model Context Protocol) server.
///
/// Supports two HTTP transport modes:
///
/// 1. Stateless POST  (compatible with HttpMcpClient in this codebase)
///    POST /mcp/messages          → JSON-RPC response returned in HTTP body
///
/// 2. SSE transport  (MCP 2024-11-05 HTTP+SSE spec)
///    GET  /mcp/sse               → SSE stream; sends `endpoint` event
///    POST /mcp/messages?session= → JSON-RPC; response pushed over SSE
///
/// Authentication: Bearer access token (infra_...) or X-API-Key header.
/// Organization context comes from the token's claims — same isolation as the REST API.
/// </summary>
[ApiController]
[Route("mcp")]
[Authorize]
public class InfraMcpController : ControllerBase
{
    // Per-session SSE channels: sessionId → channel of SSE data lines
    private static readonly ConcurrentDictionary<string, Channel<string>> SseSessions = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IHostRepository _hosts;
    private readonly IHostNoteRepository _hostNotes;
    private readonly IPolicyRepository _policies;
    private readonly IAuditRepository _audit;
    private readonly ICommandExecutor _commander;
    private readonly ISshConnectionPool _sshPool;

    public InfraMcpController(
        IHostRepository hosts,
        IHostNoteRepository hostNotes,
        IPolicyRepository policies,
        IAuditRepository audit,
        ICommandExecutor commander,
        ISshConnectionPool sshPool)
    {
        _hosts = hosts;
        _hostNotes = hostNotes;
        _policies = policies;
        _audit = audit;
        _commander = commander;
        _sshPool = sshPool;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SSE endpoint — clients connect here first to receive the message endpoint
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("sse")]
    public async Task Sse(CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        SseSessions[sessionId] = channel;

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            // Send the messages endpoint URL
            var messagesUrl = $"/mcp/messages?session={sessionId}";
            await Response.WriteAsync($"event: endpoint\ndata: {messagesUrl}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            // Stream responses until client disconnects or server shuts down
            await foreach (var line in channel.Reader.ReadAllAsync(ct))
            {
                await Response.WriteAsync(line, ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        finally
        {
            SseSessions.TryRemove(sessionId, out _);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JSON-RPC message handler — works in both stateless and SSE mode
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("messages")]
    public async Task<IActionResult> Messages(
        [FromQuery] string? session,
        CancellationToken ct)
    {
        JsonObject? rpc;
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            rpc = JsonNode.Parse(doc.RootElement.GetRawText()) as JsonObject;
        }
        catch
        {
            return BadRequest(new { error = "Invalid JSON" });
        }

        if (rpc == null)
            return BadRequest(new { error = "Expected JSON object" });

        var method = rpc["method"]?.GetValue<string>() ?? string.Empty;
        var id = rpc["id"]; // null for notifications

        // Notifications have no id and expect no response
        if (id == null && method.StartsWith("notifications/"))
        {
            // Accepted silently
            return Accepted();
        }

        var @params = rpc["params"] as JsonObject;
        JsonObject result;

        try
        {
            result = await DispatchAsync(method, @params, ct);
        }
        catch (Exception ex)
        {
            var errResponse = BuildErrorResponse(id, -32603, ex.Message);
            return await SendResponseAsync(session, errResponse, ct);
        }

        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result
        };

        return await SendResponseAsync(session, response, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JSON-RPC dispatch
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<JsonObject> DispatchAsync(string method, JsonObject? @params, CancellationToken ct) =>
        method switch
        {
            "initialize" => HandleInitialize(@params),
            "tools/list" => HandleToolsList(),
            "tools/call" => await HandleToolCallAsync(@params, ct),
            _ => throw new InvalidOperationException($"Method not found: {method}")
        };

    private static JsonObject HandleInitialize(JsonObject? _) => new()
    {
        ["protocolVersion"] = "2024-11-05",
        ["capabilities"] = new JsonObject
        {
            ["tools"] = new JsonObject()
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "InfraLLM",
            ["version"] = "1.0"
        }
    };

    private static JsonObject HandleToolsList() => new()
    {
        ["tools"] = new JsonArray(ToolDefinitions.Select(t => (JsonNode)t.Schema.DeepClone()).ToArray())
    };

    private async Task<JsonObject> HandleToolCallAsync(JsonObject? @params, CancellationToken ct)
    {
        var name = @params?["name"]?.GetValue<string>() ?? string.Empty;
        var args = @params?["arguments"] as JsonObject ?? new JsonObject();

        var orgId = GetOrganizationId();
        var userId = GetUserId();

        var text = name switch
        {
            "list_hosts" => await ListHostsAsync(orgId, args, ct),
            "get_host_details" => await GetHostDetailsAsync(orgId, args, ct),
            "execute_command" => await ExecuteCommandAsync(userId, orgId, args, ct),
            "test_host_connection" => await TestHostConnectionAsync(orgId, args, ct),
            "list_policies" => await ListPoliciesAsync(orgId, ct),
            "get_audit_logs" => await GetAuditLogsAsync(orgId, args, ct),
            _ => $"Unknown tool: {name}"
        };

        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text
            })
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tool implementations
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string> ListHostsAsync(Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hosts = await _hosts.GetByOrganizationAsync(orgId, ct);

        // Optional environment filter
        var env = args["environment"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(env))
            hosts = hosts.Where(h => h.Environment?.Equals(env, StringComparison.OrdinalIgnoreCase) == true).ToList();

        if (hosts.Count == 0)
            return "No hosts found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {hosts.Count} host(s):\n");
        foreach (var h in hosts)
        {
            sb.AppendLine($"- **{h.Name}** (`{h.Id}`)");
            sb.AppendLine($"  Hostname: {h.Hostname}:{h.Port}  |  Environment: {h.Environment ?? "—"}  |  Status: {h.Status}");
            if (h.Tags?.Count > 0)
                sb.AppendLine($"  Tags: {string.Join(", ", h.Tags)}");
        }
        return sb.ToString().Trim();
    }

    private async Task<string> GetHostDetailsAsync(Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        var note = await _hostNotes.GetByHostIdAsync(orgId, hostId, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"## {host.Name}");
        sb.AppendLine($"- **ID**: {host.Id}");
        sb.AppendLine($"- **Hostname**: {host.Hostname}:{host.Port}");
        sb.AppendLine($"- **Environment**: {host.Environment ?? "—"}");
        sb.AppendLine($"- **Status**: {host.Status}");
        sb.AppendLine($"- **Last health check**: {host.LastHealthCheck?.ToString("u") ?? "never"}");
        sb.AppendLine($"- **Type**: {host.Type}");
        if (host.Tags?.Count > 0)
            sb.AppendLine($"- **Tags**: {string.Join(", ", host.Tags)}");
        if (note != null)
        {
            sb.AppendLine();
            sb.AppendLine("### Operational Notes");
            sb.AppendLine(note.Content);
            sb.AppendLine($"_(last updated {note.UpdatedAt:u})_");
        }
        return sb.ToString().Trim();
    }

    private async Task<string> ExecuteCommandAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var command = args["command"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command is required.";

        var dryRun = args["dry_run"]?.GetValue<bool>() ?? false;

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        try
        {
            var result = await _commander.ExecuteAsync(userId, hostId, command, dryRun, ct);

            var sb = new StringBuilder();
            sb.AppendLine(dryRun ? "**DRY RUN** — command not executed" : $"**Executed** on {host.Name} (exit code {result.ExitCode})");
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                sb.AppendLine("```");
                sb.AppendLine(result.StandardOutput.TrimEnd());
                sb.AppendLine("```");
            }
            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                sb.AppendLine("**stderr:**");
                sb.AppendLine("```");
                sb.AppendLine(result.StandardError.TrimEnd());
                sb.AppendLine("```");
            }
            sb.AppendLine($"Duration: {result.Duration.TotalMilliseconds:F0} ms");
            return sb.ToString().Trim();
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Policy denied: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    private async Task<string> TestHostConnectionAsync(Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        try
        {
            var ok = await _sshPool.TestConnectionAsync(hostId, ct);
            return ok
                ? $"Connection to **{host.Name}** ({host.Hostname}:{host.Port}) succeeded."
                : $"Connection to **{host.Name}** ({host.Hostname}:{host.Port}) failed — check SSH credentials and firewall rules.";
        }
        catch (Exception ex)
        {
            return $"Connection to **{host.Name}** failed: {ex.Message}";
        }
    }

    private async Task<string> ListPoliciesAsync(Guid orgId, CancellationToken ct)
    {
        var policies = await _policies.GetByOrganizationAsync(orgId, ct);
        if (policies.Count == 0)
            return "No policies configured.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {policies.Count} polic(ies):\n");
        foreach (var p in policies)
        {
            sb.AppendLine($"- **{p.Name}** (`{p.Id}`) — enabled: {p.IsEnabled}");
            if (p.AllowedCommandPatterns.Count > 0)
                sb.AppendLine($"  Allow: {string.Join(", ", p.AllowedCommandPatterns.Take(5))}");
            if (p.DeniedCommandPatterns.Count > 0)
                sb.AppendLine($"  Deny:  {string.Join(", ", p.DeniedCommandPatterns.Take(5))}");
        }
        return sb.ToString().Trim();
    }

    private async Task<string> GetAuditLogsAsync(Guid orgId, JsonObject args, CancellationToken ct)
    {
        var limitArg = args["limit"]?.GetValue<int>() ?? 20;
        var limit = Math.Clamp(limitArg, 1, 100);

        Guid? hostId = null;
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (Guid.TryParse(hostIdStr, out var parsedHostId))
            hostId = parsedHostId;

        var (items, total) = await _audit.SearchAsync(
            orgId,
            hostId: hostId,
            pageSize: limit,
            ct: ct);

        if (items.Count == 0)
            return "No audit log entries found.";

        var sb = new StringBuilder();
        sb.AppendLine($"Showing {items.Count} of {total} audit event(s):\n");
        foreach (var e in items)
        {
            sb.AppendLine($"- [{e.Timestamp:u}] **{e.EventType}**");
            if (!string.IsNullOrEmpty(e.Command))
                sb.AppendLine($"  Command: `{e.Command}`");
            if (e.HostId.HasValue)
                sb.AppendLine($"  Host: {e.HostId}");
        }
        return sb.ToString().Trim();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<IActionResult> SendResponseAsync(
        string? sessionId, JsonObject response, CancellationToken ct)
    {
        var json = response.ToJsonString(JsonOpts);

        // SSE mode: push response to the SSE channel
        if (!string.IsNullOrEmpty(sessionId) && SseSessions.TryGetValue(sessionId, out var channel))
        {
            var sseData = $"event: message\ndata: {json}\n\n";
            await channel.Writer.WriteAsync(sseData, ct);
            return Accepted();
        }

        // Stateless mode: return directly in HTTP response
        return Content(json, "application/json");
    }

    private static JsonObject BuildErrorResponse(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        }
    };

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token");

    private Guid GetOrganizationId()
    {
        var claim = User.FindFirstValue("org_id");
        if (Guid.TryParse(claim, out var id)) return id;
        throw new UnauthorizedAccessException("Organization ID not found in token");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tool schema definitions
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<(string Name, JsonObject Schema)> ToolDefinitions =
    [
        ("list_hosts", BuildSchema("list_hosts",
            "List all managed hosts in your organization. Optionally filter by environment.",
            new Dictionary<string, JsonObject>
            {
                ["environment"] = Prop("string", "Filter by environment (e.g. production, staging)")
            })),

        ("get_host_details", BuildSchema("get_host_details",
            "Get detailed information about a specific host, including its operational notes.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID")
            },
            required: ["host_id"])),

        ("execute_command", BuildSchema("execute_command",
            "Execute a shell command on a managed host via SSH. Respects all configured command policies. Use dry_run=true to check whether the command would be permitted without executing it.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["command"] = Prop("string", "The shell command to run"),
                ["dry_run"] = Prop("boolean", "If true, validate policy without executing (default false)")
            },
            required: ["host_id", "command"])),

        ("test_host_connection", BuildSchema("test_host_connection",
            "Test SSH connectivity to a host. Returns success or failure with diagnostic info.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID")
            },
            required: ["host_id"])),

        ("list_policies", BuildSchema("list_policies",
            "List all command policies configured in your organization, including allowed and denied command patterns.",
            new Dictionary<string, JsonObject>())),

        ("get_audit_logs", BuildSchema("get_audit_logs",
            "Retrieve recent audit log entries. Optionally filter by host.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "Filter to a specific host GUID (optional)"),
                ["limit"] = Prop("integer", "Maximum entries to return (1-100, default 20)")
            })),
    ];

    private static JsonObject Prop(string type, string description) => new()
    {
        ["type"] = type,
        ["description"] = description
    };

    private static JsonObject BuildSchema(
        string name,
        string description,
        Dictionary<string, JsonObject> properties,
        string[]? required = null)
    {
        var propsNode = new JsonObject();
        foreach (var (key, val) in properties)
            propsNode[key] = val.DeepClone();

        var inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propsNode
        };

        if (required?.Length > 0)
            inputSchema["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());

        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = inputSchema
        };
    }
}
