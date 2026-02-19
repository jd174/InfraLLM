using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Infrastructure.Services;

public class SshCommandExecutor : ICommandExecutor
{
    private readonly ISshConnectionPool _connectionPool;
    private readonly IPolicyService _policyService;
    private readonly IAuditLogger _auditLogger;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SshCommandExecutor> _logger;

    public SshCommandExecutor(
        ISshConnectionPool connectionPool,
        IPolicyService policyService,
        IAuditLogger auditLogger,
        ApplicationDbContext db,
        ILogger<SshCommandExecutor> logger)
    {
        _connectionPool = connectionPool;
        _policyService = policyService;
        _auditLogger = auditLogger;
        _db = db;
        _logger = logger;
    }

    public async Task<CommandResult> ExecuteAsync(
        string userId,
        Guid hostId,
        string command,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var host = await _db.Hosts.FindAsync([hostId], ct)
            ?? throw new InvalidOperationException($"Host {hostId} not found");

        if (host.Type != InfraLLM.Core.Enums.HostType.SSH)
        {
            return new CommandResult
            {
                ExecutionId = Guid.NewGuid(),
                Command = command,
                ExitCode = -1,
                StandardError = $"Host type {host.Type} does not support SSH command execution.",
                ExecutedAt = DateTime.UtcNow,
                WasDryRun = dryRun
            };
        }

        var isMember = await _db.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == host.OrganizationId, ct);

        if (!isMember)
            throw new UnauthorizedAccessException("User is not a member of this organization");

        // Validate against policy
        var validation = await _policyService.ValidateCommandAsync(userId, hostId, command, ct);
        if (!validation.IsAllowed)
        {
            await _auditLogger.LogCommandDeniedAsync(
                host.OrganizationId, userId, null, hostId, host.Name, command, validation.DenialReason!, ct);

            return new CommandResult
            {
                ExecutionId = Guid.NewGuid(),
                Command = command,
                ExitCode = -1,
                StandardError = $"Command denied: {validation.DenialReason}",
                ExecutedAt = DateTime.UtcNow,
                WasDryRun = dryRun
            };
        }

        if (dryRun)
        {
            return new CommandResult
            {
                ExecutionId = Guid.NewGuid(),
                Command = command,
                ExitCode = 0,
                StandardOutput = $"[DRY RUN] Command would execute: {command}",
                ExecutedAt = DateTime.UtcNow,
                WasDryRun = true
            };
        }

        var sw = Stopwatch.StartNew();
        var client = (SshClient)await _connectionPool.GetConnectionAsync(hostId, ct);

        using var cmd = client.CreateCommand(command);
        cmd.CommandTimeout = TimeSpan.FromMinutes(5);

        var stdout = cmd.Execute();
        var stderr = cmd.Error;
        sw.Stop();

        var exitCode = cmd.ExitStatus ?? 0;

        var execution = new CommandExecution
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HostId = hostId,
            Command = command,
            ExitCode = exitCode,
            StandardOutput = stdout,
            StandardError = stderr,
            DurationMs = (int)sw.ElapsedMilliseconds,
            WasDryRun = false,
            ExecutedAt = DateTime.UtcNow
        };

        _db.CommandExecutions.Add(execution);
        await _db.SaveChangesAsync(ct);

        var result = new CommandResult
        {
            ExecutionId = execution.Id,
            Command = command,
            ExitCode = exitCode,
            StandardOutput = stdout,
            StandardError = stderr,
            Duration = sw.Elapsed,
            ExecutedAt = execution.ExecutedAt,
            WasDryRun = false
        };

        await _auditLogger.LogCommandExecutedAsync(
            host.OrganizationId, userId, null, hostId, host.Name, command, result, ct: ct);

        return result;
    }

    public async IAsyncEnumerable<string> StreamCommandOutputAsync(
        string userId,
        Guid hostId,
        string command,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var host = await _db.Hosts.FindAsync([hostId], ct)
            ?? throw new InvalidOperationException($"Host {hostId} not found");

        if (host.Type != InfraLLM.Core.Enums.HostType.SSH)
        {
            yield return $"ERROR: Host type {host.Type} does not support execute_command.";
            yield break;
        }

        var isMember = await _db.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == host.OrganizationId, ct);

        if (!isMember)
        {
            yield return "ERROR: Forbidden";
            yield break;
        }

        var validation = await _policyService.ValidateCommandAsync(userId, hostId, command, ct);
        if (!validation.IsAllowed)
        {
            yield return $"ERROR: Command denied - {validation.DenialReason}";
            yield break;
        }

        var client = (SshClient)await _connectionPool.GetConnectionAsync(hostId, ct);
        using var shellStream = client.CreateShellStream("xterm", 120, 40, 800, 600, 4096);

        shellStream.WriteLine(command);

        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested)
        {
            if (shellStream.DataAvailable)
            {
                var read = shellStream.Read(buffer, 0, buffer.Length);
                if (read > 0)
                {
                    yield return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            else
            {
                await Task.Delay(100, ct);
            }
        }
    }
}
