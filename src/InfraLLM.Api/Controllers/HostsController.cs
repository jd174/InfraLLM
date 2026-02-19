using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Core.Enums;
using HostModel = InfraLLM.Core.Models.Host;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HostsController : ControllerBase
{
    private readonly IHostRepository _hostRepo;
    private readonly ISshConnectionPool _connectionPool;
    private readonly IAuditLogger _auditLogger;
    private readonly IJobRepository _jobRepo;
    private readonly IHostNoteRepository _hostNoteRepo;
    private readonly IPolicyRepository _policyRepo;
    private readonly IPromptSettingsRepository _promptRepo;
    private readonly ILlmService _llmService;

    private const string DailyHostNotesJobName = "Daily Host Notes";
    private const string DailyHostNotesCron = "0 2 * * *";

    public HostsController(
        IHostRepository hostRepo,
        ISshConnectionPool connectionPool,
        IAuditLogger auditLogger,
        IJobRepository jobRepo,
        IHostNoteRepository hostNoteRepo,
        IPolicyRepository policyRepo,
        IPromptSettingsRepository promptRepo,
        ILlmService llmService)
    {
        _hostRepo = hostRepo;
        _connectionPool = connectionPool;
        _auditLogger = auditLogger;
        _jobRepo = jobRepo;
        _hostNoteRepo = hostNoteRepo;
        _policyRepo = policyRepo;
        _promptRepo = promptRepo;
        _llmService = llmService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var orgId = GetOrganizationId();
        var hosts = await _hostRepo.GetByOrganizationAsync(orgId, ct);
        return Ok(hosts);
    }

    [HttpGet("notes")]
    public async Task<IActionResult> GetNotes([FromQuery] string? hostIds, CancellationToken ct)
    {
        var orgId = GetOrganizationId();
        var ids = ParseHostIds(hostIds);
        if (ids.Count == 0)
            return Ok(new List<HostNoteResponse>());

        var notes = await _hostNoteRepo.GetByHostIdsAsync(orgId, ids, ct);
        var response = notes
            .Select(n => new HostNoteResponse
            {
                HostId = n.HostId,
                Content = n.Content,
                UpdatedAt = n.UpdatedAt
            })
            .ToList();

        return Ok(response);
    }

    [HttpPost("{id:guid}/notes/refresh")]
    public async Task<IActionResult> RefreshNotes(Guid id, CancellationToken ct)
    {
        var host = await _hostRepo.GetByIdAsync(id, ct);
        if (host == null) return NotFound();
        if (host.OrganizationId != GetOrganizationId()) return Forbid();

        var promptSettings = await _promptRepo.GetByUserAsync(host.OrganizationId, GetUserId(), ct);
        var policies = await _policyRepo.GetByOrganizationAsync(host.OrganizationId, ct);
        var existing = await _hostNoteRepo.GetByHostIdAsync(host.OrganizationId, host.Id, ct);

        var prompt = BuildHostNotesPrompt(host, existing?.Content, "Update and maintain concise operational notes for this host.");
        var response = await _llmService.SendMessageAsync(
            GetUserId(),
            host.Id.ToString(),
            host.OrganizationId,
            prompt,
            new List<HostModel> { host },
            policies,
            new List<Message>(),
            promptSettings?.SystemPrompt,
            promptSettings?.PersonalizationPrompt,
            promptSettings?.DefaultModel,
            ct);

        var note = new HostNote
        {
            OrganizationId = host.OrganizationId,
            HostId = host.Id,
            Content = response.Content.Trim(),
            UpdatedByUserId = GetUserId()
        };

        var saved = await _hostNoteRepo.UpsertAsync(note, ct);

        return Ok(new HostNoteResponse
        {
            HostId = saved.HostId,
            Content = saved.Content,
            UpdatedAt = saved.UpdatedAt
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var host = await _hostRepo.GetByIdAsync(id, ct);
        if (host == null) return NotFound();
        if (host.OrganizationId != GetOrganizationId()) return Forbid();
        return Ok(host);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateHostRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Host name is required" });
        if (string.IsNullOrWhiteSpace(request.Hostname))
            return BadRequest(new { error = "Hostname is required" });
            
        var host = new InfraLLM.Core.Models.Host
        {
            OrganizationId = GetOrganizationId(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            Hostname = request.Hostname,
            Port = request.Port,
            Username = request.Username,
            CredentialId = request.CredentialId,
            AllowInsecureSsl = request.AllowInsecureSsl,
            Tags = request.Tags ?? [],
            Environment = request.Environment,
            Status = HealthStatus.Unknown,
            CreatedBy = GetUserId()
        };

        var created = await _hostRepo.CreateAsync(host, ct);
        await EnsureDailyHostNotesJobAsync(host.OrganizationId, GetUserId(), ct);
        await _auditLogger.LogEventAsync(host.OrganizationId, GetUserId(), AuditEventType.HostAdded, $"Host created: {host.Name}", ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateHostRequest request, CancellationToken ct)
    {
        var host = await _hostRepo.GetByIdAsync(id, ct);
        if (host == null) return NotFound();
        if (host.OrganizationId != GetOrganizationId()) return Forbid();

        var hostnameChanged = request.Hostname != null && request.Hostname != host.Hostname;
        var portChanged = request.Port.HasValue && request.Port.Value != host.Port;
        var usernameChanged = request.Username != null && request.Username != host.Username;
        var credentialChanged = request.CredentialId.HasValue && request.CredentialId.Value != host.CredentialId;

        host.Name = request.Name ?? host.Name;
        host.Description = request.Description ?? host.Description;
        host.Hostname = request.Hostname ?? host.Hostname;
        host.Port = request.Port ?? host.Port;
        host.Username = request.Username ?? host.Username;
        host.CredentialId = request.CredentialId ?? host.CredentialId;
        host.AllowInsecureSsl = request.AllowInsecureSsl ?? host.AllowInsecureSsl;
        host.Tags = request.Tags ?? host.Tags;
        host.Environment = request.Environment ?? host.Environment;

        var updated = await _hostRepo.UpdateAsync(host, ct);

        if (hostnameChanged || portChanged || usernameChanged || credentialChanged)
            await _connectionPool.InvalidateHostAsync(id);

        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var host = await _hostRepo.GetByIdAsync(id, ct);
        if (host == null) return NotFound();
        if (host.OrganizationId != GetOrganizationId()) return Forbid();

        await _hostRepo.DeleteAsync(id, ct);
        await _connectionPool.InvalidateHostAsync(id);
        await _auditLogger.LogEventAsync(host.OrganizationId, GetUserId(), AuditEventType.HostRemoved, $"Host deleted: {host.Name}", ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test-connection")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var host = await _hostRepo.GetByIdAsync(id, ct);
        if (host == null) return NotFound();
        if (host.OrganizationId != GetOrganizationId()) return Forbid();

        var success = await _connectionPool.TestConnectionAsync(id, ct);
        return Ok(new { success, hostId = id, message = success ? "SSH connection successful" : "SSH connection failed" });
    }

    [HttpGet("{id:guid}/health")]
    public async Task<IActionResult> GetHealth(Guid id, CancellationToken ct)
    {
        var host = await _hostRepo.GetByIdAsync(id, ct);
        if (host == null) return NotFound();
        if (host.OrganizationId != GetOrganizationId()) return Forbid();

        return Ok(new { host.Status, host.LastHealthCheck });
    }

    private Guid GetOrganizationId()
    {
        var claim = User.FindFirst("org_id")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new UnauthorizedAccessException("No organization context");
    }

    private string GetUserId()
        => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? throw new UnauthorizedAccessException();

    private async Task EnsureDailyHostNotesJobAsync(Guid organizationId, string userId, CancellationToken ct)
    {
        var existing = await _jobRepo.GetByOrganizationAndNameAsync(organizationId, DailyHostNotesJobName, ct);
        if (existing != null) return;

        var job = new Job
        {
            OrganizationId = organizationId,
            UserId = userId,
            Name = DailyHostNotesJobName,
            Description = "Daily LLM-maintained notes per host",
            TriggerType = JobTriggerType.Cron,
            CronSchedule = DailyHostNotesCron,
            AutoRunLlm = true,
            IsEnabled = true,
            Prompt = "Update and maintain concise operational notes for each host based on its role, environment, and recent changes."
        };

        await _jobRepo.CreateAsync(job, ct);
    }

    private static List<Guid> ParseHostIds(string? hostIds)
    {
        if (string.IsNullOrWhiteSpace(hostIds)) return [];
        return hostIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
    }

    private static string BuildHostNotesPrompt(HostModel host, string? existingNotes, string? basePrompt)
    {
        var promptLines = new List<string>
        {
            basePrompt ?? "Update and maintain concise operational notes for this host.",
            $"Host: {host.Name} ({host.Type}) {host.Hostname}:{host.Port}",
            string.IsNullOrWhiteSpace(host.Environment) ? string.Empty : $"Environment: {host.Environment}",
            host.Tags.Count == 0 ? string.Empty : $"Tags: {string.Join(", ", host.Tags)}",
            string.IsNullOrWhiteSpace(existingNotes) ? "Existing notes: (none)" : $"Existing notes: {existingNotes}",
            "Return updated notes as concise bullet points. Avoid secrets."
        };

        return string.Join("\n", promptLines.Where(l => !string.IsNullOrWhiteSpace(l)));
    }
}

public class HostNoteResponse
{
    public Guid HostId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public record CreateHostRequest(
    string Name,
    string? Description,
    HostType Type,
    string Hostname,
    int Port,
    string? Username,
    Guid? CredentialId,
    bool AllowInsecureSsl,
    List<string>? Tags,
    string? Environment);

public record UpdateHostRequest(
    string? Name,
    string? Description,
    string? Hostname,
    int? Port,
    string? Username,
    Guid? CredentialId,
    bool? AllowInsecureSsl,
    List<string>? Tags,
    string? Environment);
