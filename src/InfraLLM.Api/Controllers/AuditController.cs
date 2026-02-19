using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Enums;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AuditController : ControllerBase
{
    private readonly IAuditRepository _auditRepo;

    public AuditController(IAuditRepository auditRepo)
    {
        _auditRepo = auditRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? userId,
        [FromQuery] Guid? hostId,
        [FromQuery] AuditEventType? eventType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? command,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var orgId = GetOrganizationId();
        var (items, totalCount) = await _auditRepo.SearchAsync(
            orgId, userId, hostId, eventType, from, to, command, page, pageSize, ct);

        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var log = await _auditRepo.GetByIdAsync(id, ct);
        if (log == null) return NotFound();
        if (log.OrganizationId != GetOrganizationId()) return Forbid();
        return Ok(log);
    }

    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ExportRequest request, CancellationToken ct)
    {
        var orgId = GetOrganizationId();
        var (items, _) = await _auditRepo.SearchAsync(
            orgId, request.UserId, request.HostId, request.EventType,
            request.From, request.To, request.Command, 1, 10000, ct);

        // TODO: implement CSV/JSON export
        return Ok(items);
    }

    private Guid GetOrganizationId()
    {
        var claim = User.FindFirst("org_id")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new UnauthorizedAccessException("No organization context");
    }
}

public record ExportRequest(
    string? UserId,
    Guid? HostId,
    AuditEventType? EventType,
    DateTime? From,
    DateTime? To,
    string? Command,
    string Format = "json");
