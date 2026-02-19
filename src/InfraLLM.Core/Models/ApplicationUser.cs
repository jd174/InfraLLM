using Microsoft.AspNetCore.Identity;

namespace InfraLLM.Core.Models;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public List<OrganizationMember> OrganizationMemberships { get; set; } = [];
}
