using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/jobs/webhook")]
public class JobsWebhookController : ControllerBase
{
    private readonly IJobRepository _jobRepo;
    private readonly ISessionRepository _sessionRepo;
    private readonly IHostRepository _hostRepo;
    private readonly IPolicyRepository _policyRepo;
    private readonly ILlmService _llmService;
    private readonly IPromptSettingsRepository _promptRepo;

    public JobsWebhookController(
        IJobRepository jobRepo,
        ISessionRepository sessionRepo,
        IHostRepository hostRepo,
        IPolicyRepository policyRepo,
        ILlmService llmService,
        IPromptSettingsRepository promptRepo)
    {
        _jobRepo = jobRepo;
        _sessionRepo = sessionRepo;
        _hostRepo = hostRepo;
        _policyRepo = policyRepo;
        _llmService = llmService;
        _promptRepo = promptRepo;
    }

    [AllowAnonymous]
    [HttpPost("{id:guid}")]
    public async Task<IActionResult> Receive(Guid id, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdAsync(id, ct);
        if (job == null) return NotFound();
        if (job.TriggerType != JobTriggerType.Webhook) return BadRequest(new { error = "Job is not configured for webhook" });
        if (!job.IsEnabled) return StatusCode(423, new { error = "Job is disabled" });

        var secret = Request.Query["secret"].ToString();
        if (string.IsNullOrWhiteSpace(secret))
            secret = Request.Headers["X-Webhook-Secret"].ToString();

        if (!string.IsNullOrWhiteSpace(job.WebhookSecret) && job.WebhookSecret != secret)
            return Unauthorized(new { error = "Invalid webhook secret" });

        string payload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            payload = await reader.ReadToEndAsync(ct);
        }

        var run = new JobRun
        {
            JobId = job.Id,
            TriggeredBy = "webhook",
            Status = "received",
            Payload = payload
        };

        run = await _jobRepo.AddRunAsync(run, ct);

        if (job.AutoRunLlm)
        {
            var session = new Session
            {
                OrganizationId = job.OrganizationId,
                UserId = job.UserId,
                Title = $"Job: {job.Name} ({DateTime.UtcNow:yyyy-MM-dd HH:mm})",
                IsJobRunSession = true
            };

            session = await _sessionRepo.CreateAsync(session, ct);

            var userMessage = new Message
            {
                SessionId = session.Id,
                Role = "user",
                Content = $"Webhook event received for job '{job.Name}':\n\n{payload}"
            };

            await _sessionRepo.AddMessageAsync(userMessage, ct);

            var hosts = await _hostRepo.GetByOrganizationAsync(job.OrganizationId, ct);
            var policies = await _policyRepo.GetByOrganizationAsync(job.OrganizationId, ct);
            var promptSettings = await _promptRepo.GetByUserAsync(job.OrganizationId, job.UserId, ct);

            var response = await _llmService.SendMessageAsync(
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
            await _sessionRepo.AddMessageAsync(assistantMessage, ct);

            run.Status = "completed";
            run.CompletedAt = DateTime.UtcNow;
            run.SessionId = session.Id;
            run.Response = response.Content;

            job.LastRunAt = run.CompletedAt;
            await _jobRepo.UpdateAsync(job, ct);
            await _jobRepo.UpdateRunAsync(run, ct);
        }

        return Ok(new { runId = run.Id });
    }
}
