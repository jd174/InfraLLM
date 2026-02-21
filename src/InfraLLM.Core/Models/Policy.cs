namespace InfraLLM.Core.Models;

public class Policy
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string> AllowedCommandPatterns { get; set; } = [];
    public List<string> DeniedCommandPatterns { get; set; } = [];
    public int MaxConcurrentCommands { get; set; } = 5;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public List<UserPolicy> UserPolicies { get; set; } = [];
}
