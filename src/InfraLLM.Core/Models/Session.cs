namespace InfraLLM.Core.Models;

public class Session
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<Guid> HostIds { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public bool IsJobRunSession { get; set; }
    public int TotalTokens { get; set; }
    public decimal TotalCost { get; set; }

    public Organization Organization { get; set; } = null!;
    public List<Message> Messages { get; set; } = [];
}
