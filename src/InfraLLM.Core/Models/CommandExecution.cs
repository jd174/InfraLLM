namespace InfraLLM.Core.Models;

public class CommandExecution
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public Guid HostId { get; set; }
    public string Command { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string? StandardOutput { get; set; }
    public string? StandardError { get; set; }
    public int? DurationMs { get; set; }
    public bool WasDryRun { get; set; }
    public DateTime ExecutedAt { get; set; }

    public Session? Session { get; set; }
    public Host Host { get; set; } = null!;
}
