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
    private readonly TimeZoneInfo _timezone = TimeZoneInfo.Utc;
    private readonly HashSet<Guid> _runningJobs = new();

    private const string DailyHostNotesJobName = "Daily Host Notes";

    public JobsCronHostedService(
        IServiceProvider serviceProvider,
        ILogger<JobsCronHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
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

        foreach (var job in jobs)
        {
            if (job.TriggerType != JobTriggerType.Cron || string.IsNullOrWhiteSpace(job.CronSchedule))
                continue;

            if (string.IsNullOrWhiteSpace(job.Prompt))
                continue;

            if (_runningJobs.Contains(job.Id))
                continue;

            CronExpression? expression;
            try
            {
                expression = CronExpression.Parse(job.CronSchedule);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid cron schedule for job {JobId}", job.Id);
                continue;
            }

            var last = job.LastRunAt ?? now.AddMinutes(-1);
            var next = expression.GetNextOccurrence(last, _timezone);
            if (next == null || next > now)
                continue;

            _runningJobs.Add(job.Id);
            _ = RunJobAsync(job, jobRepo, sessionRepo, hostRepo, hostNoteRepo, policyRepo, promptRepo, llmService, now, ct)
                .ContinueWith(_ => _runningJobs.Remove(job.Id), ct);
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
        var run = new JobRun
        {
            JobId = job.Id,
            TriggeredBy = "cron",
            Status = "received",
            Payload = job.Prompt ?? string.Empty
        };

        run = await jobRepo.AddRunAsync(run, ct);

        try
        {
            if (string.Equals(job.Name, DailyHostNotesJobName, StringComparison.OrdinalIgnoreCase))
            {
                var summary = await RunHostNotesJobAsync(job, hostRepo, hostNoteRepo, policyRepo, promptRepo, llmService, ct);
                run.Status = "completed";
                run.CompletedAt = DateTime.UtcNow;
                run.Response = summary;
                job.LastRunAt = run.CompletedAt;
                await jobRepo.UpdateAsync(job, ct);
                await jobRepo.UpdateRunAsync(run, ct);
                return;
            }

            var session = new Session
            {
                OrganizationId = job.OrganizationId,
                UserId = job.UserId,
                Title = $"Job: {job.Name} ({now:yyyy-MM-dd HH:mm})",
                IsJobRunSession = true
            };

            session = await sessionRepo.CreateAsync(session, ct);

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
            run.Status = "failed";
            run.CompletedAt = DateTime.UtcNow;
            run.Response = ex.Message;
            await jobRepo.UpdateRunAsync(run, ct);
            _logger.LogError(ex, "Cron job {JobId} failed", job.Id);
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

            var note = new HostNote
            {
                OrganizationId = job.OrganizationId,
                HostId = host.Id,
                Content = response.Content.Trim(),
                UpdatedByUserId = job.UserId
            };

            await hostNoteRepo.UpsertAsync(note, ct);
            updates.Add($"{host.Name}: updated");
        }

        return updates.Count == 0 ? "No notes updated." : string.Join("; ", updates);
    }
}
