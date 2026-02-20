using Cronos;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Api.Services;

public class JobsCronHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobsCronHostedService> _logger;
    private readonly TimeZoneInfo _timezone;
    private readonly HashSet<Guid> _runningJobs = new();

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
                var scopedPolicyRepo = jobScope.ServiceProvider.GetRequiredService<IPolicyRepository>();
                var scopedPromptRepo = jobScope.ServiceProvider.GetRequiredService<IPromptSettingsRepository>();
                var scopedLlmService = jobScope.ServiceProvider.GetRequiredService<ILlmService>();
                try
                {
                    await RunJobAsync(capturedJob, scopedJobRepo, scopedSessionRepo, scopedHostRepo, scopedPolicyRepo, scopedPromptRepo, scopedLlmService, now, ct);
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

}
