using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobRepository _jobRepo;

    public JobsController(IJobRepository jobRepo)
    {
        _jobRepo = jobRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var jobs = await _jobRepo.GetByOrganizationAsync(GetOrganizationId(), ct);
        return Ok(jobs);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdAsync(id, ct);
        if (job == null) return NotFound();
        if (job.OrganizationId != GetOrganizationId()) return Forbid();
        return Ok(job);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJobRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Job name is required" });

        if (request.TriggerType == JobTriggerType.Cron && string.IsNullOrWhiteSpace(request.CronSchedule))
            return BadRequest(new { error = "Cron schedule is required for cron jobs" });

        if (request.TriggerType == JobTriggerType.Cron && string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Prompt is required for cron jobs" });

        var job = new Job
        {
            OrganizationId = GetOrganizationId(),
            UserId = GetUserId(),
            Name = request.Name,
            Description = request.Description,
            Prompt = request.Prompt,
            TriggerType = request.TriggerType,
            CronSchedule = request.TriggerType == JobTriggerType.Cron ? request.CronSchedule : null,
            WebhookSecret = request.TriggerType == JobTriggerType.Webhook
                ? (string.IsNullOrWhiteSpace(request.WebhookSecret) ? Guid.NewGuid().ToString("N") : request.WebhookSecret)
                : null,
            AutoRunLlm = request.AutoRunLlm ?? (request.TriggerType == JobTriggerType.Webhook),
            IsEnabled = request.IsEnabled ?? true
        };

        var created = await _jobRepo.CreateAsync(job, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateJobRequest request, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdAsync(id, ct);
        if (job == null) return NotFound();
        if (job.OrganizationId != GetOrganizationId()) return Forbid();

        job.Name = request.Name ?? job.Name;
        job.Description = request.Description ?? job.Description;
        job.Prompt = request.Prompt ?? job.Prompt;
        job.TriggerType = request.TriggerType ?? job.TriggerType;
        job.CronSchedule = request.CronSchedule ?? job.CronSchedule;
        job.AutoRunLlm = request.AutoRunLlm ?? job.AutoRunLlm;
        job.IsEnabled = request.IsEnabled ?? job.IsEnabled;

        if (job.TriggerType == JobTriggerType.Webhook && !string.IsNullOrWhiteSpace(request.WebhookSecret))
            job.WebhookSecret = request.WebhookSecret;

        var updated = await _jobRepo.UpdateAsync(job, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdAsync(id, ct);
        if (job == null) return NotFound();
        if (job.OrganizationId != GetOrganizationId()) return Forbid();

        await _jobRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> GetRuns(Guid id, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdAsync(id, ct);
        if (job == null) return NotFound();
        if (job.OrganizationId != GetOrganizationId()) return Forbid();

        var runs = await _jobRepo.GetRunsAsync(id, ct);
        return Ok(runs);
    }

    [HttpGet("runs")]
    public async Task<IActionResult> GetRecentRuns([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var runs = await _jobRepo.GetRecentRunsByOrganizationAsync(GetOrganizationId(), Math.Clamp(limit, 1, 200), ct);
        var result = runs.Select(r => new JobRunSummary
        {
            Id = r.Id,
            JobId = r.JobId,
            JobName = r.Job.Name,
            SessionId = r.SessionId,
            TriggeredBy = r.TriggeredBy,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            Response = r.Response
        }).ToList();

        return Ok(result);
    }

    private Guid GetOrganizationId()
    {
        var claim = User.FindFirst("org_id")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new UnauthorizedAccessException("No organization context");
    }

    private string GetUserId()
        => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? throw new UnauthorizedAccessException();
}

public class CreateJobRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Prompt { get; set; }
    public JobTriggerType TriggerType { get; set; }
    public string? CronSchedule { get; set; }
    public string? WebhookSecret { get; set; }
    public bool? AutoRunLlm { get; set; }
    public bool? IsEnabled { get; set; }
}

public class UpdateJobRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Prompt { get; set; }
    public JobTriggerType? TriggerType { get; set; }
    public string? CronSchedule { get; set; }
    public string? WebhookSecret { get; set; }
    public bool? AutoRunLlm { get; set; }
    public bool? IsEnabled { get; set; }
}

public class JobRunSummary
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string JobName { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public string TriggeredBy { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Response { get; set; }
}
