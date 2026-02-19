using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InfraLLM.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfraLLM.Infrastructure.Services.Mcp;

/// <summary>
/// A single log entry captured from a stdio MCP server process.
/// </summary>
public sealed record McpLogEntry(DateTime Timestamp, string Level, string Message);

/// <summary>
/// MCP client that communicates with a local process via stdio (stdin/stdout).
/// Implements JSON-RPC 2.0 over newline-delimited JSON, matching the MCP stdio transport spec.
/// </summary>
public sealed class StdioMcpClient : IMcpClient
{
    private readonly Process _process;
    private readonly ILogger<StdioMcpClient> _logger;
    private readonly string _serverName;

    // Optional callback invoked for every log-worthy event (stderr lines, lifecycle, errors).
    // Used by StdioMcpClientCache to maintain a per-server ring buffer.
    private readonly Action<McpLogEntry>? _logSink;

    // Pending requests waiting for a response, keyed by request ID
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject?>> _pending = new();

    // Background reader tasks that pump stdout/stderr → pending completions / log sink
    private readonly Task _readerTask;
    private readonly Task _stderrTask;
    private readonly CancellationTokenSource _readerCts = new();

    private bool _initialized;
    private bool _disposed;

    /// <summary>True if the underlying process has exited (crashed or was killed).</summary>
    public bool HasProcessExited => _process.HasExited;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StdioMcpClient(Process process, ILogger<StdioMcpClient> logger, string serverName,
        Action<McpLogEntry>? logSink = null)
    {
        _process = process;
        _logger = logger;
        _serverName = serverName;
        _logSink = logSink;

        // Start background readers immediately so we don't miss early messages
        _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token));
        _stderrTask = Task.Run(() => StderrLoopAsync(_readerCts.Token));
    }

    private void Emit(string level, string message)
    {
        _logSink?.Invoke(new McpLogEntry(DateTime.UtcNow, level, message));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var response = await SendRpcAsync("tools/list", null, ct);
        if (response == null)
            return [];

        var tools = new List<McpTool>();

        if (response["tools"] is JsonArray toolArray)
        {
            foreach (var toolNode in toolArray)
            {
                if (toolNode is not JsonObject tool) continue;

                var name = tool["name"]?.GetValue<string>() ?? string.Empty;
                var description = tool["description"]?.GetValue<string>() ?? string.Empty;
                var schema = tool["inputSchema"] as JsonObject
                    ?? new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };

                if (!string.IsNullOrEmpty(name))
                    tools.Add(new McpTool(name, description, schema));
            }
        }

        _logger.LogInformation("Discovered {Count} tools from stdio MCP server '{Name}'", tools.Count, _serverName);
        return tools;
    }

    public async Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        var @params = new JsonObject
        {
            ["name"] = toolName,
            ["arguments"] = arguments.DeepClone()
        };

        var response = await SendRpcAsync("tools/call", @params, ct);

        if (response == null)
            return $"Error: No response from stdio MCP server '{_serverName}' for tool {toolName}";

        // MCP tool call response: { content: [{ type: "text", text: "..." }] }
        if (response["content"] is JsonArray contentArray)
        {
            var sb = new StringBuilder();
            foreach (var item in contentArray)
            {
                if (item is not JsonObject contentItem) continue;
                var type = contentItem["type"]?.GetValue<string>();
                if (type == "text")
                    sb.AppendLine(contentItem["text"]?.GetValue<string>() ?? "");
                else if (type == "error")
                    sb.AppendLine($"Error: {contentItem["text"]?.GetValue<string>()}");
            }
            return sb.ToString().Trim();
        }

        return response.ToJsonString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancel the background reader
        await _readerCts.CancelAsync();

        // Fail any still-pending requests
        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
        _pending.Clear();

        // Gracefully close stdin so the process knows we're done
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();

                // Give the process a moment to exit cleanly
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await _process.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { /* timed out — kill it */ }
            }
        }
        catch { /* process may already be gone */ }
        finally
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); }
                catch { /* best effort */ }
            }

            _process.Dispose();
        }

        try { await _readerTask; }
        catch { /* reader will throw OperationCanceledException — ignore */ }

        try { await _stderrTask; }
        catch { /* same */ }

        _readerCts.Dispose();
    }

    // ── Initialization ────────────────────────────────────────────────────────

    // uvx/npx may need to download the package AND compile Python bytecode on the very first
    // cold-start inside Docker.  Bytecode compilation of 89+ packages on a Docker overlay2
    // filesystem can take several minutes.  We give the handshake a dedicated 5-minute window
    // that is independent of the HTTP request's cancellation token (nginx cuts at ~150 s, but
    // the warmup service runs with its own long-lived token so this effectively applies there).
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromMinutes(5);

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        // Send minimal capabilities — some servers (e.g. ha-mcp) reject unknown capability
        // keys like "roots" or "sampling" with "Invalid request parameters".
        var initParams = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "InfraLLM",
                ["version"] = "1.0"
            }
        };

        // Use a dedicated timeout for the initialize handshake so that a slow uvx/npx
        // cold-start doesn't get cancelled by the upstream HTTP request's shorter deadline.
        using var initCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        initCts.CancelAfter(InitializeTimeout);

        _logger.LogInformation(
            "Sending MCP initialize to '{Name}' (timeout {Timeout}s)",
            _serverName, (int)InitializeTimeout.TotalSeconds);
        Emit("info", $"Sending initialize handshake (timeout {(int)InitializeTimeout.TotalSeconds}s)…");

        var initResponse = await SendRpcAsync("initialize", initParams, initCts.Token);
        if (initResponse == null)
        {
            _logger.LogWarning("stdio MCP initialize returned null from '{Name}'", _serverName);
            Emit("warn", "Initialize returned null — server may not support this protocol version");
        }
        else
        {
            Emit("info", "Initialize handshake complete ✓");
            // Send initialized notification (no response expected)
            await SendNotificationAsync("notifications/initialized", ct);
        }

        _initialized = true;
    }

    // ── JSON-RPC transport ────────────────────────────────────────────────────

    /// <summary>
    /// Writes a JSON-RPC request to stdin and waits for the matching response from the reader loop.
    /// </summary>
    private async Task<JsonObject?> SendRpcAsync(string method, JsonObject? @params, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];

        var rpcRequest = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = requestId,
            ["method"] = method
        };

        if (@params != null)
            rpcRequest["params"] = @params.DeepClone();

        // Register a completion source before writing so we can't miss the response
        var tcs = new TaskCompletionSource<JsonObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        try
        {
            await WriteLineAsync(rpcRequest.ToJsonString(), ct);

            // Wait for the reader loop to deliver the response
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _readerCts.Token);
            linked.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("stdio MCP request '{Method}' cancelled for '{Name}'", method, _serverName);
            Emit("warn", $"Request '{method}' cancelled (timeout or shutdown)");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "stdio MCP request '{Method}' failed for '{Name}'", method, _serverName);
            Emit("error", $"Request '{method}' failed: {ex.Message}");
            return null;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    /// <summary>
    /// Writes a JSON-RPC notification (no response expected).
    /// </summary>
    private async Task SendNotificationAsync(string method, CancellationToken ct)
    {
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        try
        {
            await WriteLineAsync(notification.ToJsonString(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send stdio MCP notification '{Method}' to '{Name}'", method, _serverName);
        }
    }

    private async Task WriteLineAsync(string json, CancellationToken ct)
    {
        await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    // ── Background stdout reader ──────────────────────────────────────────────

    /// <summary>
    /// Continuously reads newline-delimited JSON from the process stdout and
    /// routes each message to the appropriate pending request.
    /// Runs on a dedicated background task for the lifetime of the client.
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct);

                if (line == null)
                {
                    // EOF — process closed stdout
                    _logger.LogInformation("stdio MCP server '{Name}' closed its stdout", _serverName);
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                DispatchResponse(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — swallow
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "stdio MCP reader loop error for '{Name}'", _serverName);
        }
        finally
        {
            // If the reader exits unexpectedly, fail all pending requests
            foreach (var tcs in _pending.Values)
                tcs.TrySetException(new IOException($"stdio MCP server '{_serverName}' disconnected unexpectedly"));
        }
    }

    /// <summary>
    /// Parses one line of JSON and routes it to the waiting request, if any.
    /// Notifications (no "id") are logged and ignored.
    /// </summary>
    private void DispatchResponse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // Notifications have no "id" — log stderr/log messages and skip
            if (!root.TryGetProperty("id", out var idProp))
            {
                // Forward server log notifications to our logger
                if (root.TryGetProperty("method", out var method))
                {
                    var methodName = method.GetString();
                    if (methodName is "notifications/message" or "notifications/stderr")
                    {
                        if (root.TryGetProperty("params", out var p) &&
                            p.TryGetProperty("data", out var data))
                        {
                            _logger.LogDebug("[{Name}] {Message}", _serverName, data.GetRawText());
                        }
                    }
                }
                return;
            }

            var requestId = idProp.GetString();
            if (requestId == null || !_pending.TryGetValue(requestId, out var tcs))
                return;

            if (root.TryGetProperty("error", out var error))
            {
                var errorMsg = error.TryGetProperty("message", out var msg)
                    ? msg.GetString()
                    : "Unknown RPC error";
                _logger.LogError("stdio MCP RPC error from '{Name}': {Error}", _serverName, errorMsg);
                Emit("error", $"RPC error: {errorMsg}");
                tcs.TrySetResult(null);
                return;
            }

            if (root.TryGetProperty("result", out var result))
            {
                var resultObj = JsonNode.Parse(result.GetRawText()) as JsonObject;
                tcs.TrySetResult(resultObj);
                return;
            }

            // No result or error — resolve with empty object so the caller can proceed
            tcs.TrySetResult(new JsonObject());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "stdio MCP received non-JSON line from '{Name}': {Line}", _serverName, line);
        }
    }

    // ── Background stderr reader ──────────────────────────────────────────────

    /// <summary>
    /// Reads stderr from the process and forwards each line to the log sink.
    /// This prevents stderr from being silently swallowed and gives users
    /// visibility into download/startup messages from uvx/npx.
    /// </summary>
    private async Task StderrLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_process.HasExited)
            {
                var line = await _process.StandardError.ReadLineAsync(ct);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                _logger.LogDebug("[{Name}] stderr: {Line}", _serverName, line);
                Emit("stderr", line);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "stdio MCP stderr reader exited for '{Name}'", _serverName);
        }
    }
}
