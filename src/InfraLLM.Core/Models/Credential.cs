using InfraLLM.Core.Enums;

namespace InfraLLM.Core.Models;

public class Credential
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CredentialType CredentialType { get; set; }
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public Organization Organization { get; set; } = null!;
}
