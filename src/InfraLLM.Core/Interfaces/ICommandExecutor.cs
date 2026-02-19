namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public class CommandResult
{
    public Guid ExecutionId { get; set; }
    public string Command { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public DateTime ExecutedAt { get; set; }
    public bool WasDryRun { get; set; }
}

public interface ICommandExecutor
{
    Task<CommandResult> ExecuteAsync(
        string userId,
        Guid hostId,
        string command,
        bool dryRun = false,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamCommandOutputAsync(
        string userId,
        Guid hostId,
        string command,
        CancellationToken ct = default);
}
