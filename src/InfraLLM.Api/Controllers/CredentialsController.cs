using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Core.Enums;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CredentialsController : ControllerBase
{
    private readonly ICredentialRepository _credentialRepo;
    private readonly IAuditLogger _auditLogger;
    private readonly ICredentialEncryptionService _credentialEncryption;

    public CredentialsController(
        ICredentialRepository credentialRepo,
        IAuditLogger auditLogger,
        ICredentialEncryptionService credentialEncryption)
    {
        _credentialRepo = credentialRepo;
        _auditLogger = auditLogger;
        _credentialEncryption = credentialEncryption;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var orgId = GetOrganizationId();
        var credentials = await _credentialRepo.GetByOrganizationAsync(orgId, ct);
        var safe = credentials.Select(c => new
        {
            c.Id,
            c.Name,
            c.CredentialType,
            c.CreatedAt,
            c.CreatedBy
        });
        return Ok(safe);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var credential = await _credentialRepo.GetByIdAsync(id, ct);
        if (credential == null) return NotFound();
        if (credential.OrganizationId != GetOrganizationId()) return Forbid();
        return Ok(new
        {
            credential.Id,
            credential.Name,
            credential.CredentialType,
            credential.CreatedAt,
            credential.CreatedBy
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCredentialRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Credential name is required" });
        if (string.IsNullOrWhiteSpace(request.Value))
            return BadRequest(new { error = "Credential value is required" });

        var credential = new Credential
        {
            OrganizationId = GetOrganizationId(),
            Name = request.Name,
            CredentialType = request.CredentialType,
            EncryptedValue = _credentialEncryption.Encrypt(request.Value),
            CreatedBy = GetUserId()
        };

        var created = await _credentialRepo.CreateAsync(credential, ct);
        await _auditLogger.LogEventAsync(
            credential.OrganizationId, GetUserId(),
            AuditEventType.CredentialAdded, $"Credential created: {credential.Name}", ct);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, new
        {
            created.Id,
            created.Name,
            created.CredentialType,
            created.CreatedAt,
            created.CreatedBy
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var credential = await _credentialRepo.GetByIdAsync(id, ct);
        if (credential == null) return NotFound();
        if (credential.OrganizationId != GetOrganizationId()) return Forbid();

        await _credentialRepo.DeleteAsync(id, ct);
        await _auditLogger.LogEventAsync(
            credential.OrganizationId, GetUserId(),
            AuditEventType.CredentialRemoved, $"Credential deleted: {credential.Name}", ct);
        return NoContent();
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

public record CreateCredentialRequest(
    string Name,
    CredentialType CredentialType,
    string Value);
