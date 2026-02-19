using System.Text.Json.Nodes;
using InfraLLM.Core.Models;

namespace InfraLLM.Core.Interfaces;

public record McpTool(
    string Name,
    string Description,
    JsonObject InputSchema);

public interface IMcpClient : IAsyncDisposable
{
    Task<IReadOnlyList<McpTool>> ListToolsAsync(CancellationToken ct = default);
    Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken ct = default);
}

public interface IMcpClientFactory
{
    Task<IMcpClient> CreateAsync(McpServer server, CancellationToken ct = default);
}
