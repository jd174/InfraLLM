namespace InfraLLM.Core.Models;

public class JobRun
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid? SessionId { get; set; }
    public string TriggeredBy { get; set; } = string.Empty; // webhook, cron, manual
    public string Status { get; set; } = string.Empty; // received, completed, failed
    public string Payload { get; set; } = string.Empty;
    public string? Response { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Job Job { get; set; } = null!;
    public Session? Session { get; set; }
}
