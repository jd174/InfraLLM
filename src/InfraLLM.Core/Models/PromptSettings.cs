namespace InfraLLM.Core.Models;

public class PromptSettings
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
    public string? PersonalizationPrompt { get; set; }
    public string? DefaultModel { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
