using System.Diagnostics;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using Microsoft.Extensions.Logging;

namespace InfraLLM.Infrastructure.Services.Mcp;

public class McpClientFactory : IMcpClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialEncryptionService _encryptionService;
    private readonly ILoggerFactory _loggerFactory;

    public McpClientFactory(
        IHttpClientFactory httpClientFactory,
        ICredentialEncryptionService encryptionService,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _encryptionService = encryptionService;
        _loggerFactory = loggerFactory;
    }

    public Task<IMcpClient> CreateAsync(McpServer server, CancellationToken ct = default)
        => CreateAsync(server, logSink: null, ct);

    /// <summary>
    /// Creates a client with an optional log sink for stdio servers.
    /// Each log entry (stderr lines, lifecycle events) is forwarded to <paramref name="logSink"/>.
    /// </summary>
    public Task<IMcpClient> CreateAsync(McpServer server, Action<McpLogEntry>? logSink, CancellationToken ct = default)
    {
        IMcpClient client = server.TransportType switch
        {
            McpTransportType.Http => CreateHttpClient(server),
            McpTransportType.Stdio => CreateStdioClient(server, logSink),
            _ => throw new ArgumentOutOfRangeException(nameof(server.TransportType),
                $"Unknown MCP transport type: {server.TransportType}")
        };

        return Task.FromResult(client);
    }

    private HttpMcpClient CreateHttpClient(McpServer server)
    {
        if (string.IsNullOrWhiteSpace(server.BaseUrl))
            throw new InvalidOperationException($"MCP server '{server.Name}' has no BaseUrl configured.");

        var httpClient = _httpClientFactory.CreateClient("McpClient");
        var logger = _loggerFactory.CreateLogger<HttpMcpClient>();

        string? apiKey = null;
        if (!string.IsNullOrEmpty(server.ApiKeyEncrypted))
        {
            try
            {
                apiKey = _encryptionService.Decrypt(server.ApiKeyEncrypted);
            }
            catch
            {
                // If decryption fails, proceed without API key
                logger.LogWarning("Failed to decrypt API key for MCP server '{Name}'", server.Name);
            }
        }

        return new HttpMcpClient(httpClient, logger, server.BaseUrl, apiKey);
    }

    private StdioMcpClient CreateStdioClient(McpServer server, Action<McpLogEntry>? logSink = null)
    {
        if (string.IsNullOrWhiteSpace(server.Command))
            throw new InvalidOperationException($"MCP server '{server.Name}' has no Command configured.");

        var startInfo = new ProcessStartInfo
        {
            FileName = server.Command,
            Arguments = server.Arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(server.WorkingDirectory)
                ? null
                : server.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,  // capture stderr so it doesn't bleed into stdout
            CreateNoWindow = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        // Ensure Python subprocesses don't block-buffer stdout when running as a pipe.
        // Without this, Python's default block-buffering mode causes MCP JSON-RPC responses
        // to sit in an internal buffer instead of being flushed to our reader immediately.
        // UV_COMPILE_BYTECODE tells uv to pre-compile .pyc files during install, avoiding
        // slow on-demand compilation that can stall the first request for minutes in Docker.
        startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        startInfo.EnvironmentVariables["UV_COMPILE_BYTECODE"] = "1";

        // Merge server-defined environment variables into the process environment
        // (applied after defaults so user values can override if needed)
        foreach (var (key, value) in server.EnvironmentVariables)
            startInfo.EnvironmentVariables[key] = value;

        var process = new Process { StartInfo = startInfo };

        var logger = _loggerFactory.CreateLogger<StdioMcpClient>();

        try
        {
            if (!process.Start())
                throw new InvalidOperationException(
                    $"Failed to start stdio MCP server process for '{server.Name}' (Command: {server.Command}).");
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"Could not start stdio MCP server '{server.Name}': {ex.Message}", ex);
        }

        logger.LogInformation(
            "Started stdio MCP server '{Name}' (PID {Pid}): {Command} {Args}",
            server.Name, process.Id, server.Command, server.Arguments);

        return new StdioMcpClient(process, logger, server.Name, logSink);
    }
}
