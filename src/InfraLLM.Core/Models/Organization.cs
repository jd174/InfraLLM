namespace InfraLLM.Core.Models;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public List<Host> Hosts { get; set; } = [];
    public List<Policy> Policies { get; set; } = [];
    public List<OrganizationMember> Members { get; set; } = [];
}
