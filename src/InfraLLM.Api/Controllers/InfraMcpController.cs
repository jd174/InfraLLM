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
/// Supports three HTTP transport modes:
///
/// 1. Streamable HTTP  (current MCP spec, 2025-03-26 and later)
///    POST   /mcp                 → JSON-RPC request/response in HTTP body
///    GET    /mcp                 → 405 (no server-initiated streaming offered)
///    DELETE /mcp                 → explicit session termination
///    An Mcp-Session-Id header is issued on initialize and validated on
///    subsequent requests when the client presents it.
///
/// 2. HTTP+SSE transport  (deprecated MCP 2024-11-05 spec, kept for back-compat)
///    GET  /mcp/sse               → SSE stream; sends `endpoint` event
///    POST /mcp/messages?session= → JSON-RPC; response pushed over SSE
///
/// 3. Stateless POST  (compatible with HttpMcpClient in this codebase)
///    POST /mcp/messages          → JSON-RPC response returned in HTTP body
///
/// Authentication: Bearer access token (infra_...) or X-API-Key header.
/// Organization context comes from the token's claims — same isolation as the REST API.
/// Access tokens may carry scopes (mcp:read / mcp:execute / mcp:write) that
/// restrict which tools are listed and callable; tokens without scopes retain
/// full access for backward compatibility.
/// </summary>
[ApiController]
[Route("mcp")]
[Authorize]
public class InfraMcpController : ControllerBase
{
    private const string LatestProtocolVersion = "2025-06-18";
    private static readonly string[] SupportedProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private const string SessionIdHeader = "Mcp-Session-Id";
    private const string ProtocolVersionHeader = "MCP-Protocol-Version";

    // ── Streamable HTTP sessions (issued on initialize, in-memory only) ──────
    private sealed class StreamSession
    {
        public required string UserId { get; init; }
        public required Guid OrganizationId { get; init; }
        public DateTime LastSeenUtc { get; set; }
    }

    private static readonly ConcurrentDictionary<string, StreamSession> StreamSessions = new();
    private static readonly TimeSpan StreamSessionIdleTimeout = TimeSpan.FromHours(24);
    private const int MaxStreamSessions = 1000;

    // ── Legacy SSE sessions: sessionId → channel of SSE data lines ───────────
    private sealed class SseSession
    {
        public required Channel<string> Channel { get; init; }
        public required string UserId { get; init; }
        public required Guid OrganizationId { get; init; }
    }

    private static readonly ConcurrentDictionary<string, SseSession> SseSessions = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>JSON-RPC protocol-level error carrying a spec error code.</summary>
    private sealed class McpException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

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
    // Streamable HTTP transport (MCP 2025-03-26+)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> StreamableHttpPost(CancellationToken ct)
    {
        // 2025-06-18 clients declare the negotiated version on every request.
        var declaredVersion = Request.Headers[ProtocolVersionHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(declaredVersion) && !SupportedProtocolVersions.Contains(declaredVersion))
        {
            return JsonRpcResult(
                BuildErrorResponse(null, -32600,
                    $"Unsupported protocol version: {declaredVersion} (supported: {string.Join(", ", SupportedProtocolVersions)})"),
                StatusCodes.Status400BadRequest);
        }

        JsonNode? node;
        try
        {
            node = await JsonNode.ParseAsync(Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            return JsonRpcResult(
                BuildErrorResponse(null, -32700, "Parse error: invalid JSON"),
                StatusCodes.Status400BadRequest);
        }

        if (node is JsonArray)
        {
            // JSON-RPC batching was removed in MCP 2025-06-18 and is not supported here.
            return JsonRpcResult(
                BuildErrorResponse(null, -32600, "JSON-RPC batching is not supported"),
                StatusCodes.Status400BadRequest);
        }

        if (node is not JsonObject rpc)
        {
            return JsonRpcResult(
                BuildErrorResponse(null, -32600, "Expected a JSON-RPC message object"),
                StatusCodes.Status400BadRequest);
        }

        var method = AsString(rpc["method"]);
        var id = rpc["id"];

        // Notifications (no id) and client → server responses (no method) get 202.
        if (method == null || id == null)
            return Accepted();

        var isInitialize = method == "initialize";

        // Validate the session header when the client presents one. Unknown or
        // expired ids get 404 so spec-compliant clients transparently re-initialize.
        var sessionId = Request.Headers[SessionIdHeader].FirstOrDefault();
        if (!isInitialize && !string.IsNullOrEmpty(sessionId) && !TryTouchStreamSession(sessionId))
        {
            return JsonRpcResult(
                BuildErrorResponse(id, -32001, "Session not found or expired — re-initialize"),
                StatusCodes.Status404NotFound);
        }

        var response = await ExecuteRequestAsync(method, id, rpc["params"] as JsonObject, ct);

        if (isInitialize && response["error"] == null)
            Response.Headers[SessionIdHeader] = CreateStreamSession();

        return JsonRpcResult(response, StatusCodes.Status200OK);
    }

    [HttpGet]
    public IActionResult StreamableHttpGet()
    {
        // This server never initiates messages outside a request, so the
        // standalone SSE stream is not offered. 405 is the spec-defined answer.
        Response.Headers.Allow = "POST, DELETE";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    [HttpDelete]
    public IActionResult StreamableHttpDelete()
    {
        var sessionId = Request.Headers[SessionIdHeader].FirstOrDefault();
        if (string.IsNullOrEmpty(sessionId))
            return BadRequest(new { error = $"{SessionIdHeader} header is required" });

        if (StreamSessions.TryGetValue(sessionId, out var session) &&
            session.UserId == GetUserId() &&
            session.OrganizationId == GetOrganizationId())
        {
            StreamSessions.TryRemove(sessionId, out _);
            return NoContent();
        }

        return NotFound();
    }

    private string CreateStreamSession()
    {
        EvictStaleStreamSessions();

        var sessionId = Guid.NewGuid().ToString("N");
        StreamSessions[sessionId] = new StreamSession
        {
            UserId = GetUserId(),
            OrganizationId = GetOrganizationId(),
            LastSeenUtc = DateTime.UtcNow
        };
        return sessionId;
    }

    /// <summary>Returns true when the session exists, is owned by the caller, and is not idle-expired.</summary>
    private bool TryTouchStreamSession(string sessionId)
    {
        if (!StreamSessions.TryGetValue(sessionId, out var session))
            return false;

        if (DateTime.UtcNow - session.LastSeenUtc > StreamSessionIdleTimeout)
        {
            StreamSessions.TryRemove(sessionId, out _);
            return false;
        }

        // A session id is only valid for the principal that initialized it.
        if (session.UserId != GetUserId() || session.OrganizationId != GetOrganizationId())
            return false;

        session.LastSeenUtc = DateTime.UtcNow;
        return true;
    }

    private static void EvictStaleStreamSessions()
    {
        var cutoff = DateTime.UtcNow - StreamSessionIdleTimeout;
        foreach (var (key, session) in StreamSessions)
        {
            if (session.LastSeenUtc < cutoff)
                StreamSessions.TryRemove(key, out _);
        }

        // Hard cap so a misbehaving client can't grow the dictionary unbounded.
        while (StreamSessions.Count >= MaxStreamSessions)
        {
            var oldest = StreamSessions.MinBy(kv => kv.Value.LastSeenUtc);
            if (oldest.Key == null || !StreamSessions.TryRemove(oldest.Key, out _))
                break;
        }
    }

    private ContentResult JsonRpcResult(JsonObject response, int statusCode) => new()
    {
        Content = response.ToJsonString(JsonOpts),
        ContentType = "application/json",
        StatusCode = statusCode
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Legacy HTTP+SSE transport (MCP 2024-11-05) — kept for back-compat
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
        SseSessions[sessionId] = new SseSession
        {
            Channel = channel,
            UserId = GetUserId(),
            OrganizationId = GetOrganizationId()
        };

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
    // JSON-RPC message handler — legacy SSE mode and stateless mode
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("messages")]
    public async Task<IActionResult> Messages(
        [FromQuery] string? session,
        CancellationToken ct)
    {
        JsonObject? rpc;
        try
        {
            var node = await JsonNode.ParseAsync(Request.Body, cancellationToken: ct);
            rpc = node as JsonObject;
        }
        catch (JsonException)
        {
            return BadRequest(new { error = "Invalid JSON" });
        }

        if (rpc == null)
            return BadRequest(new { error = "Expected JSON object" });

        // SSE sessions are bound to the principal that opened the stream;
        // reject posts from anyone else so responses can't be injected into
        // another user's stream.
        SseSession? sseSession = null;
        if (!string.IsNullOrEmpty(session))
        {
            if (!SseSessions.TryGetValue(session, out sseSession) ||
                sseSession.UserId != GetUserId() ||
                sseSession.OrganizationId != GetOrganizationId())
            {
                return NotFound(new { error = "Unknown SSE session" });
            }
        }

        var method = AsString(rpc["method"]) ?? string.Empty;
        var id = rpc["id"];

        // Notifications have no id and expect no response
        if (id == null)
            return Accepted();

        var response = await ExecuteRequestAsync(method, id, rpc["params"] as JsonObject, ct);
        return await SendResponseAsync(sseSession, response, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // JSON-RPC dispatch (shared by all transports)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<JsonObject> ExecuteRequestAsync(string method, JsonNode? id, JsonObject? @params, CancellationToken ct)
    {
        try
        {
            var result = await DispatchAsync(method, @params, ct);
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["result"] = result
            };
        }
        catch (McpException ex)
        {
            return BuildErrorResponse(id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return BuildErrorResponse(id, -32603, ex.Message);
        }
    }

    private async Task<JsonObject> DispatchAsync(string method, JsonObject? @params, CancellationToken ct) =>
        method switch
        {
            "initialize" => HandleInitialize(@params),
            "ping" => new JsonObject(),
            "tools/list" => HandleToolsList(),
            "tools/call" => await HandleToolCallAsync(@params, ct),
            _ => throw new McpException(-32601, $"Method not found: {method}")
        };

    private static JsonObject HandleInitialize(JsonObject? @params)
    {
        // Echo the client's requested version when we support it; otherwise
        // offer our latest and let the client decide whether to continue.
        var requested = AsString(@params?["protocolVersion"]);
        var negotiated = requested != null && SupportedProtocolVersions.Contains(requested)
            ? requested
            : LatestProtocolVersion;

        return new JsonObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false
                }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "InfraLLM",
                ["title"] = "InfraLLM Infrastructure Management",
                ["version"] = "1.0"
            }
        };
    }

    private JsonObject HandleToolsList()
    {
        var scopes = GetTokenScopes();
        var visible = ToolDefinitions
            .Where(t => scopes == null || scopes.Contains(t.RequiredScope))
            .Select(t => (JsonNode)t.Schema.DeepClone());

        return new JsonObject
        {
            ["tools"] = new JsonArray(visible.ToArray())
        };
    }

    private async Task<JsonObject> HandleToolCallAsync(JsonObject? @params, CancellationToken ct)
    {
        var name = AsString(@params?["name"]) ?? string.Empty;
        var args = @params?["arguments"] as JsonObject ?? new JsonObject();

        var tool = ToolDefinitions.FirstOrDefault(t => t.Name == name);
        if (tool.Name == null)
            throw new McpException(-32602, $"Unknown tool: {name}");

        var scopes = GetTokenScopes();
        if (scopes != null && !scopes.Contains(tool.RequiredScope))
        {
            return ToolResult(
                $"Access denied: this access token does not have the '{tool.RequiredScope}' scope required by '{name}'.",
                isError: true);
        }

        var orgId = GetOrganizationId();
        var userId = GetUserId();

        string text;
        var isError = false;
        try
        {
            text = name switch
            {
                "list_hosts" => await ListHostsAsync(orgId, args, ct),
                "get_host_details" => await GetHostDetailsAsync(orgId, args, ct),
                "execute_command" => await ExecuteCommandAsync(userId, orgId, args, ct),
                "test_host_connection" => await TestHostConnectionAsync(orgId, args, ct),
                "list_policies" => await ListPoliciesAsync(orgId, ct),
                "get_audit_logs" => await GetAuditLogsAsync(orgId, args, ct),
                "tail_logs" => await TailLogsAsync(userId, orgId, args, ct),
                "update_host_notes" => await UpdateHostNotesAsync(userId, orgId, args, ct),
                "read_file" => await ReadFileAsync(userId, orgId, args, ct),
                "check_service_status" => await CheckServiceStatusAsync(userId, orgId, args, ct),
                "list_containers" => await ListContainersAsync(userId, orgId, args, ct),
                "check_container_status" => await CheckContainerStatusAsync(userId, orgId, args, ct),
                "write_file" => await WriteFileAsync(userId, orgId, args, ct),
                _ => throw new McpException(-32602, $"Unknown tool: {name}")
            };
        }
        catch (McpException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Tool execution failures are reported in-result so the model can
            // see them, per the MCP spec — not as protocol errors.
            text = $"Error: {ex.Message}";
            isError = true;
        }

        return ToolResult(text, isError);
    }

    private static JsonObject ToolResult(string text, bool isError = false)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text
            })
        };
        if (isError)
            result["isError"] = true;
        return result;
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

    private async Task<string> TailLogsAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var source = args["source"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(source))
            return "Error: source is required.";

        var lines = args["lines"]?.GetValue<int>() ?? 50;
        lines = Math.Clamp(lines, 1, 1000);
        var sourceType = args["source_type"]?.GetValue<string>() ?? "file";

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        var command = sourceType == "journald"
            ? $"journalctl -u {source} -n {lines} --no-pager"
            : $"tail -n {lines} {source}";

        try
        {
            var result = await _commander.ExecuteAsync(userId, hostId, command, false, ct);
            if (result.ExitCode != 0)
                return $"Error reading logs: {result.StandardError}";
            return result.StandardOutput ?? "(empty)";
        }
        catch (UnauthorizedAccessException ex) { return $"Policy denied: {ex.Message}"; }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<string> UpdateHostNotesAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var content = args["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
            return "Error: content is required.";

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        var note = new HostNote
        {
            OrganizationId = orgId,
            HostId = hostId,
            Content = content.Trim(),
            UpdatedByUserId = userId
        };
        await _hostNotes.UpsertAsync(note, ct);
        return $"Updated host notes for **{host.Name}** ({host.Id}).";
    }

    private async Task<string> ReadFileAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var filePath = args["file_path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(filePath))
            return "Error: file_path is required.";

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        try
        {
            var result = await _commander.ExecuteAsync(userId, hostId, $"cat {filePath}", false, ct);
            if (result.ExitCode != 0)
                return $"Error reading file: {result.StandardError}";
            return result.StandardOutput ?? "(empty)";
        }
        catch (UnauthorizedAccessException ex) { return $"Policy denied: {ex.Message}"; }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<string> CheckServiceStatusAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var serviceName = args["service_name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(serviceName))
            return "Error: service_name is required.";

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        try
        {
            var result = await _commander.ExecuteAsync(userId, hostId, $"systemctl status {serviceName}", false, ct);
            return result.StandardOutput ?? result.StandardError ?? "(no output)";
        }
        catch (UnauthorizedAccessException ex) { return $"Policy denied: {ex.Message}"; }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<string> ListContainersAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var all = args["all"]?.GetValue<bool>() ?? false;

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        var command = all
            ? "docker ps -a --format 'table {{.Names}}\\t{{.Image}}\\t{{.Status}}\\t{{.Ports}}'"
            : "docker ps --format 'table {{.Names}}\\t{{.Image}}\\t{{.Status}}\\t{{.Ports}}'";

        try
        {
            var result = await _commander.ExecuteAsync(userId, hostId, command, false, ct);
            if (result.ExitCode != 0)
                return $"Error listing containers: {result.StandardError}";
            return result.StandardOutput ?? "(no containers)";
        }
        catch (UnauthorizedAccessException ex) { return $"Policy denied: {ex.Message}"; }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<string> CheckContainerStatusAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var containerName = args["container_name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(containerName))
            return "Error: container_name is required.";

        var logLines = args["log_lines"]?.GetValue<int>() ?? 20;
        logLines = Math.Clamp(logLines, 1, 500);

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        var command = $"docker inspect --format='State: {{{{.State.Status}}}} | Started: {{{{.State.StartedAt}}}}' {containerName} 2>&1 && echo '--- Recent Logs ---' && docker logs --tail {logLines} {containerName} 2>&1";

        try
        {
            var result = await _commander.ExecuteAsync(userId, hostId, command, false, ct);
            if (result.ExitCode != 0)
                return $"Error checking container: {result.StandardError}";
            return result.StandardOutput ?? "(no output)";
        }
        catch (UnauthorizedAccessException ex) { return $"Policy denied: {ex.Message}"; }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<string> WriteFileAsync(string userId, Guid orgId, JsonObject args, CancellationToken ct)
    {
        var hostIdStr = args["host_id"]?.GetValue<string>();
        if (!Guid.TryParse(hostIdStr, out var hostId))
            return "Error: host_id must be a valid GUID.";

        var filePath = args["file_path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(filePath))
            return "Error: file_path is required.";

        var content = args["content"]?.GetValue<string>();
        if (content == null)
            return "Error: content is required.";

        var backup = args["backup"]?.GetValue<bool>() ?? true;

        var host = await _hosts.GetByIdAsync(hostId, ct);
        if (host == null || host.OrganizationId != orgId)
            return $"Error: host {hostId} not found.";

        try
        {
            var sb = new StringBuilder();

            if (backup)
            {
                var backupResult = await _commander.ExecuteAsync(
                    userId, hostId,
                    $"cp {filePath} {filePath}.bak.$(date +%Y%m%d%H%M%S) 2>/dev/null || true",
                    false, ct);
                if (backupResult.ExitCode == 0)
                    sb.AppendLine($"Backup created: {filePath}.bak.<timestamp>");
            }

            var escapedContent = content.Replace("\\", "\\\\").Replace("'", "\\'");
            var writeResult = await _commander.ExecuteAsync(
                userId, hostId, $"printf '%s' '{escapedContent}' > {filePath}", false, ct);

            if (writeResult.ExitCode != 0)
                return $"{sb}Error writing file: {writeResult.StandardError}";

            sb.AppendLine($"Successfully wrote {content.Length} characters to {filePath}");
            return sb.ToString().TrimEnd();
        }
        catch (UnauthorizedAccessException ex) { return $"Policy denied: {ex.Message}"; }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<IActionResult> SendResponseAsync(
        SseSession? sseSession, JsonObject response, CancellationToken ct)
    {
        var json = response.ToJsonString(JsonOpts);

        // SSE mode: push response to the SSE channel
        if (sseSession != null)
        {
            var sseData = $"event: message\ndata: {json}\n\n";
            await sseSession.Channel.Writer.WriteAsync(sseData, ct);
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

    private static string? AsString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token");

    private Guid GetOrganizationId()
    {
        var claim = User.FindFirstValue("org_id");
        if (Guid.TryParse(claim, out var id)) return id;
        throw new UnauthorizedAccessException("Organization ID not found in token");
    }

    /// <summary>
    /// Scopes granted to the caller, or null when unrestricted.
    /// JWT browser sessions and access tokens created without scopes are unrestricted.
    /// </summary>
    private IReadOnlySet<string>? GetTokenScopes()
    {
        if (User.FindFirstValue("auth_method") != "access_token")
            return null;

        var scope = User.FindFirstValue("scope");
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        return scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tool schema definitions
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<(string Name, string RequiredScope, JsonObject Schema)> ToolDefinitions =
    [
        ("list_hosts", AccessTokenScopes.Read, BuildSchema("list_hosts",
            "List all managed hosts in your organization. Optionally filter by environment.",
            new Dictionary<string, JsonObject>
            {
                ["environment"] = Prop("string", "Filter by environment (e.g. production, staging)")
            })),

        ("get_host_details", AccessTokenScopes.Read, BuildSchema("get_host_details",
            "Get detailed information about a specific host, including its operational notes.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID")
            },
            required: ["host_id"])),

        ("execute_command", AccessTokenScopes.Execute, BuildSchema("execute_command",
            "Execute a shell command on a managed host via SSH. Respects all configured command policies. Use dry_run=true to check whether the command would be permitted without executing it.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["command"] = Prop("string", "The shell command to run"),
                ["dry_run"] = Prop("boolean", "If true, validate policy without executing (default false)")
            },
            required: ["host_id", "command"])),

        ("test_host_connection", AccessTokenScopes.Read, BuildSchema("test_host_connection",
            "Test SSH connectivity to a host. Returns success or failure with diagnostic info.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID")
            },
            required: ["host_id"])),

        ("list_policies", AccessTokenScopes.Read, BuildSchema("list_policies",
            "List all command policies configured in your organization, including allowed and denied command patterns.",
            new Dictionary<string, JsonObject>())),

        ("get_audit_logs", AccessTokenScopes.Read, BuildSchema("get_audit_logs",
            "Retrieve recent audit log entries. Optionally filter by host.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "Filter to a specific host GUID (optional)"),
                ["limit"] = Prop("integer", "Maximum entries to return (1-100, default 20)")
            })),

        ("tail_logs", AccessTokenScopes.Read, BuildSchema("tail_logs",
            "Retrieve the most recent log entries from a log file or systemd journal on a host. Use this tool whenever you need to investigate errors, diagnose service failures, review crash output, check authentication attempts, or understand recent system activity. For log files supply the absolute path as source (e.g. /var/log/syslog, /var/log/nginx/error.log, /var/log/auth.log, /var/log/postgresql/postgresql.log) with source_type='file' (default). For systemd-managed services supply the service name as source (e.g. nginx, docker, sshd, postgresql) with source_type='journald'. Prefer this tool over read_file for any log file because it returns only the most recent lines without transferring the entire file.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["source"] = Prop("string", "File path (e.g. /var/log/syslog) or systemd service name (e.g. nginx)"),
                ["lines"] = Prop("integer", "Number of lines to return (default 50, max 1000)"),
                ["source_type"] = Prop("string", "Either 'file' (default) or 'journald'")
            },
            required: ["host_id", "source"])),

        ("update_host_notes", AccessTokenScopes.Write, BuildSchema("update_host_notes",
            "Create or replace the operational notes stored for a host. Use this to record findings, runbooks, or context about a host.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["content"] = Prop("string", "The updated notes content (markdown supported)")
            },
            required: ["host_id", "content"])),

        ("read_file", AccessTokenScopes.Read, BuildSchema("read_file",
            "Read the full contents of a file on a managed host via SSH.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["file_path"] = Prop("string", "Absolute path to the file to read")
            },
            required: ["host_id", "file_path"])),

        ("check_service_status", AccessTokenScopes.Read, BuildSchema("check_service_status",
            "Check the status of a systemd service on a managed host (runs systemctl status).",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["service_name"] = Prop("string", "Name of the systemd service (e.g. nginx, docker, sshd)")
            },
            required: ["host_id", "service_name"])),

        ("list_containers", AccessTokenScopes.Read, BuildSchema("list_containers",
            "List Docker containers on a managed host. Shows name, image, status, and ports.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["all"] = Prop("boolean", "If true, include stopped containers (default false)")
            },
            required: ["host_id"])),

        ("check_container_status", AccessTokenScopes.Read, BuildSchema("check_container_status",
            "Get the current state and recent log output for a specific Docker container.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["container_name"] = Prop("string", "Name or ID of the Docker container"),
                ["log_lines"] = Prop("integer", "Number of recent log lines to return (default 20, max 500)")
            },
            required: ["host_id", "container_name"])),

        ("write_file", AccessTokenScopes.Write, BuildSchema("write_file",
            "Write content to a file on a managed host. Creates a timestamped backup by default. Use for safe config file edits.",
            new Dictionary<string, JsonObject>
            {
                ["host_id"] = Prop("string", "The host GUID"),
                ["file_path"] = Prop("string", "Absolute path to the file to write"),
                ["content"] = Prop("string", "The content to write to the file"),
                ["backup"] = Prop("boolean", "If true (default), create a timestamped backup before overwriting")
            },
            required: ["host_id", "file_path", "content"])),
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
