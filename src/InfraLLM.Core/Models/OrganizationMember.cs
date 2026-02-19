namespace InfraLLM.Core.Models;

public class OrganizationMember
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = "Member"; // Owner, Admin, Member
    public DateTime JoinedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
