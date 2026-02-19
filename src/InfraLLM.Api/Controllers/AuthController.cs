using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using InfraLLM.Core.Models;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _config;
    private readonly ApplicationDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration config,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _db = db;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        // Create organization using user-provided name or default
        var orgName = !string.IsNullOrWhiteSpace(request.OrganizationName)
            ? request.OrganizationName
            : $"{request.DisplayName ?? request.Email}'s Organization";

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = orgName,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user.Id
        };
        _db.Organizations.Add(org);

        var membership = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            UserId = user.Id,
            Role = "Owner",
            JoinedAt = DateTime.UtcNow
        };
        _db.OrganizationMembers.Add(membership);
        await _db.SaveChangesAsync();

        var token = GenerateJwtToken(user, org.Id);
        return Ok(new AuthResponseDto(token, new UserDto(user.Id, user.Email!, user.DisplayName ?? user.Email!, org.Id.ToString())));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
            return Unauthorized(new { message = "Invalid credentials" });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid credentials" });

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Get user's first organization
        var membership = await _db.OrganizationMembers
            .FirstOrDefaultAsync(m => m.UserId == user.Id);

        if (membership == null)
            return Unauthorized(new { message = "User has no organization" });

        var token = GenerateJwtToken(user, membership.OrganizationId);
        return Ok(new AuthResponseDto(token, new UserDto(user.Id, user.Email!, user.DisplayName ?? user.Email!, membership.OrganizationId.ToString())));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var displayName = User.FindFirstValue("display_name");
        var orgId = User.FindFirstValue("org_id");

        if (userId == null || email == null)
            return Unauthorized();

        return Ok(new UserDto(userId, email, displayName ?? email, orgId ?? ""));
    }

    [Authorize]
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
            user.DisplayName = request.DisplayName.Trim();

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return NoContent();
    }

    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return NoContent();
    }

    private string GenerateJwtToken(ApplicationUser user, Guid organizationId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT secret not configured")));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim("org_id", organizationId.ToString()),
            new Claim("display_name", user.DisplayName ?? user.Email!)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(_config["Jwt:ExpirationMinutes"] ?? "60")),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public record RegisterRequest(string Email, string Password, string? DisplayName, string? OrganizationName);
public record LoginRequest(string Email, string Password);
public record UpdateProfileRequest(string? DisplayName);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserDto(string Id, string Email, string DisplayName, string OrganizationId);
public record AuthResponseDto(string Token, UserDto User);
