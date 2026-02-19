namespace InfraLLM.Core.Models;

public class UserPolicy
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid? HostId { get; set; }
    public string? HostGroup { get; set; }
    public Guid PolicyId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Policy Policy { get; set; } = null!;
    public Host? Host { get; set; }
}
