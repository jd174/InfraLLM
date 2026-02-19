using InfraLLM.Core.Enums;

namespace InfraLLM.Core.Models;

public class Host
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public HostType Type { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Username { get; set; }
    public Guid? CredentialId { get; set; }
    public bool AllowInsecureSsl { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Environment { get; set; }
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;
    public DateTime? LastHealthCheck { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public Organization Organization { get; set; } = null!;
    public Credential? Credential { get; set; }
}
