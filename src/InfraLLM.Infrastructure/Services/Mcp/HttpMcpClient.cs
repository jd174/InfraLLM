using System.Net.Http.Headers;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using InfraLLM.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace InfraLLM.Infrastructure.Services.Mcp;

/// <summary>
/// MCP client that communicates with an HTTP/SSE MCP server.
/// Implements the MCP HTTP transport: POST /messages for RPC calls.
/// </summary>
public sealed class HttpMcpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpMcpClient> _logger;
    private readonly string _baseUrl;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public HttpMcpClient(HttpClient httpClient, ILogger<HttpMcpClient> logger, string baseUrl, string? apiKey)
    {
        _httpClient = httpClient;
        _logger = logger;
        _baseUrl = baseUrl.TrimEnd('/');

        if (!string.IsNullOrEmpty(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

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

        _logger.LogInformation("Discovered {Count} tools from MCP server at {BaseUrl}", tools.Count, _baseUrl);
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
            return $"Error: No response from MCP server for tool {toolName}";

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

        // Fallback: return serialized response
        return response.ToJsonString();
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        // Send MCP initialize request
        var initParams = new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject
            {
                ["roots"] = new JsonObject { ["listChanged"] = false },
                ["sampling"] = new JsonObject()
            },
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "InfraLLM",
                ["version"] = "1.0"
            }
        };

        var initResponse = await SendRpcAsync("initialize", initParams, ct);
        if (initResponse == null)
        {
            _logger.LogWarning("MCP initialize returned null response from {BaseUrl}", _baseUrl);
        }
        else
        {
            // Send initialized notification
            await SendNotificationAsync("notifications/initialized", ct);
        }

        _initialized = true;
    }

    /// <summary>
    /// Sends a JSON-RPC 2.0 request and returns the result node.
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

        var json = rpcRequest.ToJsonString();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var httpResponse = await _httpClient.PostAsync($"{_baseUrl}/messages", content, ct);
            var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("MCP server returned {Status} for {Method}: {Body}",
                    httpResponse.StatusCode, method, responseBody);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                var errorMsg = error.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                _logger.LogError("MCP RPC error for {Method}: {Error}", method, errorMsg);
                return null;
            }

            if (root.TryGetProperty("result", out var result))
            {
                return JsonNode.Parse(result.GetRawText()) as JsonObject;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP request failed for {Method} at {BaseUrl}", method, _baseUrl);
            return null;
        }
    }

    /// <summary>
    /// Sends a JSON-RPC notification (no response expected).
    /// </summary>
    private async Task SendNotificationAsync(string method, CancellationToken ct)
    {
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        var json = notification.ToJsonString();
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            await _httpClient.PostAsync($"{_baseUrl}/messages", content, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send MCP notification {Method}", method);
        }
    }

    public ValueTask DisposeAsync()
    {
        // HttpClient is managed externally (IHttpClientFactory), nothing to dispose here
        return ValueTask.CompletedTask;
    }
}
