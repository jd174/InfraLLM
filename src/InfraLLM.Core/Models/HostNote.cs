namespace InfraLLM.Core.Models;

public class HostNote
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid HostId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string UpdatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Host Host { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}
