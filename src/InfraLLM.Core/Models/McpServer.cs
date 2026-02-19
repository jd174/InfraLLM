using InfraLLM.Core.Enums;

namespace InfraLLM.Core.Models;

public class McpServer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public McpTransportType TransportType { get; set; } = McpTransportType.Http;

    // HTTP/SSE transport
    public string? BaseUrl { get; set; }
    public string? ApiKeyEncrypted { get; set; }

    // Stdio transport
    public string? Command { get; set; }
    public string? Arguments { get; set; }
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
}
