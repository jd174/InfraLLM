using InfraLLM.Core.Enums;

namespace InfraLLM.Core.Models;

public class Job
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Prompt { get; set; }
    public JobTriggerType TriggerType { get; set; }
    public string? CronSchedule { get; set; }
    public string? WebhookSecret { get; set; }
    public bool AutoRunLlm { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastRunAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public List<JobRun> Runs { get; set; } = [];
}
