using System.Text.Json;
using Cronos;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using HostModel = InfraLLM.Core.Models.Host;

namespace InfraLLM.Api.Services;

public class JobsCronHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobsCronHostedService> _logger;
    private readonly TimeZoneInfo _timezone;
    private readonly HashSet<Guid> _runningJobs = new();

    private const string DailyHostNotesJobName = "Daily Host Notes";

    public JobsCronHostedService(
        IServiceProvider serviceProvider,
        ILogger<JobsCronHostedService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _timezone = ResolveTimeZone(configuration["Cron:TimeZone"], logger);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId, ILogger logger)
    {
        var requested = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(requested);
        }
        catch (TimeZoneNotFoundException)
        {
            logger.LogWarning("Cron timezone '{TimeZone}' not found. Falling back to UTC.", requested);
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            logger.LogWarning("Cron timezone '{TimeZone}' is invalid. Falling back to UTC.", requested);
            return TimeZoneInfo.Utc;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunDueJobsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cron scheduler loop failed");
            }
        }
    }

    private async Task RunDueJobsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var sessionRepo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var hostRepo = scope.ServiceProvider.GetRequiredService<IHostRepository>();
        var hostNoteRepo = scope.ServiceProvider.GetRequiredService<IHostNoteRepository>();
        var policyRepo = scope.ServiceProvider.GetRequiredService<IPolicyRepository>();
        var promptRepo = scope.ServiceProvider.GetRequiredService<IPromptSettingsRepository>();
        var llmService = scope.ServiceProvider.GetRequiredService<ILlmService>();

        var jobs = await jobRepo.GetEnabledCronJobsAsync(ct);
        var now = DateTime.UtcNow;
        var dueCount = 0;

        _logger.LogDebug("Cron scheduler tick at {Now}. Enabled cron jobs: {Count}", now, jobs.Count);

        foreach (var job in jobs)
        {
            if (job.TriggerType != JobTriggerType.Cron || string.IsNullOrWhiteSpace(job.CronSchedule))
            {
                _logger.LogDebug("Cron job {JobId} skipped: missing schedule", job.Id);
                continue;
            }

            if (string.IsNullOrWhiteSpace(job.Prompt))
            {
                _logger.LogDebug("Cron job {JobId} skipped: missing prompt", job.Id);
                continue;
            }

            if (_runningJobs.Contains(job.Id))
            {
                _logger.LogDebug("Cron job {JobId} skipped: already running", job.Id);
                continue;
            }

            CronExpression? expression;
            try
            {
                expression = CronExpression.Parse(job.CronSchedule);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid cron schedule for job {JobId}: {Cron}", job.Id, job.CronSchedule);
                continue;
            }

            var last = job.LastRunAt ?? now.AddMinutes(-1);
            var next = expression.GetNextOccurrence(last, _timezone);
            if (next == null)
            {
                _logger.LogWarning("Cron job {JobId} has no next occurrence. Last={Last} Cron={Cron}", job.Id, last, job.CronSchedule);
                continue;
            }

            if (next > now)
            {
                _logger.LogDebug("Cron job {JobId} not due. Last={Last} Next={Next} Now={Now}", job.Id, last, next, now);
                continue;
            }

            dueCount++;
            _logger.LogInformation("Cron job {JobId} due. Last={Last} Next={Next} Now={Now}", job.Id, last, next, now);

            // Optimistically stamp LastRunAt so subsequent ticks within the same minute don't re-fire this job
            job.LastRunAt = now;

            _runningJobs.Add(job.Id);
            var capturedJob = job;
            _ = Task.Run(async () =>
            {
                using var jobScope = _serviceProvider.CreateScope();
                var scopedJobRepo = jobScope.ServiceProvider.GetRequiredService<IJobRepository>();
                var scopedSessionRepo = jobScope.ServiceProvider.GetRequiredService<ISessionRepository>();
                var scopedHostRepo = jobScope.ServiceProvider.GetRequiredService<IHostRepository>();
                var scopedHostNoteRepo = jobScope.ServiceProvider.GetRequiredService<IHostNoteRepository>();
                var scopedPolicyRepo = jobScope.ServiceProvider.GetRequiredService<IPolicyRepository>();
                var scopedPromptRepo = jobScope.ServiceProvider.GetRequiredService<IPromptSettingsRepository>();
                var scopedLlmService = jobScope.ServiceProvider.GetRequiredService<ILlmService>();
                try
                {
                    await RunJobAsync(capturedJob, scopedJobRepo, scopedSessionRepo, scopedHostRepo, scopedHostNoteRepo, scopedPolicyRepo, scopedPromptRepo, scopedLlmService, now, ct);
                }
                finally
                {
                    _runningJobs.Remove(capturedJob.Id);
                }
            }, CancellationToken.None);
        }

        if (dueCount > 0)
        {
            _logger.LogInformation("Cron scheduler dispatched {Count} job(s) at {Now}", dueCount, now);
        }
        else
        {
            _logger.LogDebug("Cron scheduler dispatched 0 jobs at {Now}", now);
        }
    }

    private async Task RunJobAsync(
        Job job,
        IJobRepository jobRepo,
        ISessionRepository sessionRepo,
        IHostRepository hostRepo,
        IHostNoteRepository hostNoteRepo,
        IPolicyRepository policyRepo,
        IPromptSettingsRepository promptRepo,
        ILlmService llmService,
        DateTime now,
        CancellationToken ct)
    {
        JobRun? run = null;
        Session? session = null;
        try
        {
            if (string.IsNullOrWhiteSpace(job.UserId))
            {
                _logger.LogWarning("Cron job {JobId} has no user; skipping", job.Id);
                return;
            }

            run = new JobRun
            {
                JobId = job.Id,
                TriggeredBy = "cron",
                Status = "received",
                Payload = job.Prompt ?? string.Empty,
            };

            run = await jobRepo.AddRunAsync(run, ct);

            session = new Session
            {
                OrganizationId = job.OrganizationId,
                UserId = job.UserId,
                Title = $"Job: {job.Name} ({now:yyyy-MM-dd HH:mm})",
                IsJobRunSession = true
            };

            session = await sessionRepo.CreateAsync(session, ct);

            run.SessionId = session.Id;
            await jobRepo.UpdateRunAsync(run, ct);

            if (string.Equals(job.Name, DailyHostNotesJobName, StringComparison.OrdinalIgnoreCase))
            {
                var dailyUserMessage = new Message
                {
                    SessionId = session.Id,
                    Role = "user",
                    Content = job.Prompt ?? "Run daily host notes refresh."
                };

                await sessionRepo.AddMessageAsync(dailyUserMessage, ct);

                var summary = await RunHostNotesJobAsync(job, hostRepo, hostNoteRepo, policyRepo, promptRepo, llmService, ct);

                var dailyAssistantMessage = new Message
                {
                    SessionId = session.Id,
                    Role = "assistant",
                    Content = summary
                };

                await sessionRepo.AddMessageAsync(dailyAssistantMessage, ct);

                run.Status = "completed";
                run.CompletedAt = DateTime.UtcNow;
                run.Response = summary;
                job.LastRunAt = run.CompletedAt;
                await jobRepo.UpdateAsync(job, ct);
                await jobRepo.UpdateRunAsync(run, ct);
                return;
            }

            var userMessage = new Message
            {
                SessionId = session.Id,
                Role = "user",
                Content = job.Prompt ?? string.Empty
            };

            await sessionRepo.AddMessageAsync(userMessage, ct);

            var hosts = await hostRepo.GetByOrganizationAsync(job.OrganizationId, ct);
            var policies = await policyRepo.GetByOrganizationAsync(job.OrganizationId, ct);
            var promptSettings = await promptRepo.GetByUserAsync(job.OrganizationId, job.UserId, ct);

            var response = await llmService.SendMessageAsync(
                job.UserId,
                session.Id.ToString(),
                job.OrganizationId,
                userMessage.Content,
                hosts,
                policies,
                new List<Message>(),
                promptSettings?.SystemPrompt,
                promptSettings?.PersonalizationPrompt,
                promptSettings?.DefaultModel,
                ct);

            var assistantMessage = new Message
            {
                SessionId = session.Id,
                Role = "assistant",
                Content = response.Content,
                TokensUsed = response.TokensUsed
            };
            await sessionRepo.AddMessageAsync(assistantMessage, ct);

            run.Status = "completed";
            run.CompletedAt = DateTime.UtcNow;
            run.SessionId = session.Id;
            run.Response = response.Content;

            job.LastRunAt = run.CompletedAt;
            await jobRepo.UpdateAsync(job, ct);
            await jobRepo.UpdateRunAsync(run, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cron job {JobId} failed", job.Id);

            if (run != null)
            {
                run.Status = "failed";
                run.CompletedAt = DateTime.UtcNow;
                run.Response = ex.Message;
                await jobRepo.UpdateRunAsync(run, ct);
            }
        }
    }

    private static string BuildHostNotesPrompt(HostModel host, string? existingNotes, string? basePrompt)
    {
        var promptLines = new List<string>
        {
            "Update and maintain concise operational notes for this host.",
            $"Host: {host.Name} ({host.Type}) {host.Hostname}:{host.Port}",
            string.IsNullOrWhiteSpace(host.Environment) ? string.Empty : $"Environment: {host.Environment}",
            host.Tags.Count == 0 ? string.Empty : $"Tags: {string.Join(", ", host.Tags)}",
            string.IsNullOrWhiteSpace(existingNotes) ? "Existing notes: (none)" : $"Existing notes: {existingNotes}",
            "Return updated notes as concise bullet points. Avoid secrets."
        };

        if (!string.IsNullOrWhiteSpace(basePrompt))
            promptLines.Insert(1, basePrompt.Trim());

        return string.Join("\n", promptLines.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    private async Task<string> RunHostNotesJobAsync(
        Job job,
        IHostRepository hostRepo,
        IHostNoteRepository hostNoteRepo,
        IPolicyRepository policyRepo,
        IPromptSettingsRepository promptRepo,
        ILlmService llmService,
        CancellationToken ct)
    {
        var hosts = await hostRepo.GetByOrganizationAsync(job.OrganizationId, ct);
        if (hosts.Count == 0) return "No hosts found.";

        var policies = await policyRepo.GetByOrganizationAsync(job.OrganizationId, ct);
        var promptSettings = await promptRepo.GetByUserAsync(job.OrganizationId, job.UserId, ct);
        var existingNotes = await hostNoteRepo.GetByHostIdsAsync(job.OrganizationId, hosts.Select(h => h.Id).ToList(), ct);
        var notesByHost = existingNotes.ToDictionary(n => n.HostId, n => n.Content);
        var updates = new List<string>();

        foreach (var host in hosts)
        {
            notesByHost.TryGetValue(host.Id, out var priorNotes);
            var message = BuildHostNotesPrompt(host, priorNotes, job.Prompt);

            var response = await llmService.SendMessageAsync(
                job.UserId,
                host.Id.ToString(),
                job.OrganizationId,
                message,
                new List<HostModel> { host },
                policies,
                new List<Message>(),
                promptSettings?.SystemPrompt,
                promptSettings?.PersonalizationPrompt,
                promptSettings?.DefaultModel,
                ct);

            var usedToolForHost = response.ToolCalls.Any(call => IsUpdateHostNotesCallForHost(call, host.Id));
            if (!usedToolForHost)
            {
                var note = new HostNote
                {
                    OrganizationId = job.OrganizationId,
                    HostId = host.Id,
                    Content = response.Content.Trim(),
                    UpdatedByUserId = job.UserId
                };

                await hostNoteRepo.UpsertAsync(note, ct);
            }
            updates.Add($"{host.Name}: updated");
        }

        return updates.Count == 0 ? "No notes updated." : string.Join("; ", updates);
    }

    private static bool IsUpdateHostNotesCallForHost(ToolCall call, Guid hostId)
    {
        if (!string.Equals(call.ToolName, "update_host_notes", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!call.Parameters.TryGetValue("host_id", out var value))
            return false;

        if (value is JsonElement je)
            value = je.GetString();

        return Guid.TryParse(value?.ToString(), out var parsed) && parsed == hostId;
    }
}
