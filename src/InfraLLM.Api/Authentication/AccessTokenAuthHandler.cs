using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using InfraLLM.Api.Controllers;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Api.Authentication;

public class AccessTokenAuthOptions : AuthenticationSchemeOptions { }

/// <summary>
/// Authentication handler that accepts long-lived access tokens issued by
/// AccessTokensController.  Tokens are recognized via:
///   • Authorization: Bearer infra_&lt;...&gt;
///   • X-API-Key: infra_&lt;...&gt;
///   • ?api_key=infra_&lt;...&gt;  (query string — useful for SSE / MCP clients)
/// The raw token is SHA-256 hashed before the DB lookup so the plaintext
/// value is never stored.
/// </summary>
public class AccessTokenAuthHandler : AuthenticationHandler<AccessTokenAuthOptions>
{
    public const string SchemeName = "AccessToken";

    private readonly ApplicationDbContext _db;

    public AccessTokenAuthHandler(
        IOptionsMonitor<AccessTokenAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext db)
        : base(options, logger, encoder)
    {
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var rawToken = ExtractToken(Request);
        if (rawToken == null)
            return AuthenticateResult.NoResult();

        var hash = AccessTokensController.ComputeHash(rawToken);

        var token = await _db.AccessTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t =>
                t.TokenHash == hash &&
                t.IsActive &&
                (t.ExpiresAt == null || t.ExpiresAt > DateTime.UtcNow));

        if (token == null)
            return AuthenticateResult.Fail("Invalid or expired access token");

        // Update LastUsedAt without loading the full entity to minimize overhead
        await _db.AccessTokens
            .Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, DateTime.UtcNow));

        // Load the user email for the email claim
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == token.UserId)
            .Select(u => new { u.Email, u.UserName })
            .FirstOrDefaultAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, token.UserId),
            new Claim(ClaimTypes.Email, user?.Email ?? string.Empty),
            new Claim("org_id", token.OrganizationId.ToString()),
            new Claim("auth_method", "access_token"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    private static string? ExtractToken(HttpRequest request)
    {
        // 1. X-API-Key header
        var apiKey = request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
            return apiKey;

        // 2. Authorization: Bearer infra_...
        var auth = request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearer = auth["Bearer ".Length..].Trim();
            if (bearer.StartsWith("infra_", StringComparison.Ordinal))
                return bearer;
        }

        // 3. ?api_key=... query string (for SSE / MCP clients that can't set headers)
        var queryKey = request.Query["api_key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(queryKey))
            return queryKey;

        return null;
    }
}
