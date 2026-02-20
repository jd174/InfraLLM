using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Models;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/access-tokens")]
[Authorize]
public class AccessTokensController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AccessTokensController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>List all access tokens for the current user (hashes omitted).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = GetUserId();
        var orgId = GetOrganizationId();

        var tokens = await _db.AccessTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.OrganizationId == orgId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AccessTokenResponse(
                t.Id,
                t.Name,
                t.CreatedAt,
                t.ExpiresAt,
                t.LastUsedAt,
                t.IsActive))
            .ToListAsync(ct);

        return Ok(tokens);
    }

    /// <summary>Create a new long-lived access token. The raw token is returned only once.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccessTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Token name is required" });

        var userId = GetUserId();
        var orgId = GetOrganizationId();

        // Generate a cryptographically random token: "infra_" prefix + 48 random bytes base64url
        var rawBytes = RandomNumberGenerator.GetBytes(48);
        var rawToken = "infra_" + Convert.ToBase64String(rawBytes)
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var hash = ComputeHash(rawToken);

        var token = new AccessToken
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            TokenHash = hash,
            UserId = userId,
            OrganizationId = orgId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt,
            IsActive = true
        };

        _db.AccessTokens.Add(token);
        await _db.SaveChangesAsync(ct);

        // Return the raw token only once — it cannot be retrieved again
        return Ok(new CreateAccessTokenResponse(
            token.Id,
            token.Name,
            rawToken,
            token.CreatedAt,
            token.ExpiresAt));
    }

    /// <summary>Revoke (deactivate) an access token.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();

        var token = await _db.AccessTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct);

        if (token == null)
            return NotFound();

        token.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    internal static string ComputeHash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID not found in token");

    private Guid GetOrganizationId()
    {
        var claim = User.FindFirstValue("org_id");
        if (Guid.TryParse(claim, out var id)) return id;
        throw new UnauthorizedAccessException("Organization ID not found in token");
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record AccessTokenResponse(
    Guid Id,
    string Name,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    bool IsActive);

public record CreateAccessTokenRequest(
    string Name,
    DateTime? ExpiresAt);

public record CreateAccessTokenResponse(
    Guid Id,
    string Name,
    string Token,
    DateTime CreatedAt,
    DateTime? ExpiresAt);
