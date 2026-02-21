using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PoliciesController : ControllerBase
{
    private readonly IPolicyRepository _policyRepo;
    private readonly IPolicyService _policyService;
    private readonly ApplicationDbContext _db;

    public PoliciesController(
        IPolicyRepository policyRepo,
        IPolicyService policyService,
        ApplicationDbContext db)
    {
        _policyRepo = policyRepo;
        _policyService = policyService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var orgId = GetOrganizationId();
        var policies = await _policyRepo.GetByOrganizationAsync(orgId, ct);
        return Ok(policies);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var policy = await _policyRepo.GetByIdAsync(id, ct);
        if (policy == null) return NotFound();
        if (policy.OrganizationId != GetOrganizationId()) return Forbid();
        return Ok(policy);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePolicyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Policy name is required" });

        var policy = new Policy
        {
            OrganizationId = GetOrganizationId(),
            Name = request.Name,
            Description = request.Description,
            AllowedCommandPatterns = request.AllowedCommandPatterns ?? [],
            DeniedCommandPatterns = request.DeniedCommandPatterns ?? [],
            MaxConcurrentCommands = request.MaxConcurrentCommands,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _policyRepo.CreateAsync(policy, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreatePolicyRequest request, CancellationToken ct)
    {
        var policy = await _policyRepo.GetByIdAsync(id, ct);
        if (policy == null) return NotFound();
        if (policy.OrganizationId != GetOrganizationId()) return Forbid();

        policy.Name = request.Name;
        policy.Description = request.Description;
        policy.AllowedCommandPatterns = request.AllowedCommandPatterns ?? [];
        policy.DeniedCommandPatterns = request.DeniedCommandPatterns ?? [];
        policy.MaxConcurrentCommands = request.MaxConcurrentCommands;
        policy.UpdatedAt = DateTime.UtcNow;

        var updated = await _policyRepo.UpdateAsync(policy, ct);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var policy = await _policyRepo.GetByIdAsync(id, ct);
        if (policy == null) return NotFound();
        if (policy.OrganizationId != GetOrganizationId()) return Forbid();

        await _policyRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---- Test Command (both routes for compat) ----

    [HttpPost("test")]
    public async Task<IActionResult> TestPolicyGlobal([FromBody] TestPolicyRequest request, CancellationToken ct)
    {
        var result = await _policyService.TestCommandAsync(request.PolicyId, request.Command, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestPolicy(Guid id, [FromBody] TestPolicyCommandRequest request, CancellationToken ct)
    {
        var result = await _policyService.TestCommandAsync(id, request.Command, ct);
        return Ok(result);
    }

    // ---- Policy Assignments (UserPolicy CRUD) ----

    [HttpGet("{id:guid}/assignments")]
    public async Task<IActionResult> GetAssignments(Guid id, CancellationToken ct)
    {
        var policy = await _policyRepo.GetByIdAsync(id, ct);
        if (policy == null) return NotFound();
        if (policy.OrganizationId != GetOrganizationId()) return Forbid();

        var assignments = await _db.UserPolicies
            .Include(up => up.Host)
            .Where(up => up.PolicyId == id)
            .Select(up => new PolicyAssignmentDto
            {
                Id = up.Id,
                UserId = up.UserId,
                HostId = up.HostId,
                HostName = up.Host != null ? up.Host.Name : null,
                CreatedAt = up.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(assignments);
    }

    [HttpPost("{id:guid}/assignments")]
    public async Task<IActionResult> CreateAssignment(Guid id, [FromBody] CreateAssignmentRequest request, CancellationToken ct)
    {
        var policy = await _policyRepo.GetByIdAsync(id, ct);
        if (policy == null) return NotFound();
        if (policy.OrganizationId != GetOrganizationId()) return Forbid();

        var orgId = GetOrganizationId();
        var callerUserId = GetUserId();

        // Default to assigning to the current user if no userId provided
        var targetUserId = request.UserId ?? callerUserId;

        // Only org Owner/Admin can assign policies to other users
        if (targetUserId != callerUserId)
        {
            var callerRole = await _db.OrganizationMembers
                .AsNoTracking()
                .Where(m => m.OrganizationId == orgId && m.UserId == callerUserId)
                .Select(m => m.Role)
                .FirstOrDefaultAsync(ct);

            if (callerRole is not ("Owner" or "Admin"))
                return Forbid();

            var targetIsMember = await _db.OrganizationMembers
                .AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.UserId == targetUserId, ct);

            if (!targetIsMember)
                return BadRequest(new { error = "Target user is not a member of this organization" });
        }

        // Validate host belongs to org (if specified)
        if (request.HostId.HasValue)
        {
            var host = await _db.Hosts.FindAsync([request.HostId.Value], ct);
            if (host == null || host.OrganizationId != GetOrganizationId())
                return BadRequest(new { error = "Host not found or does not belong to this organization" });
        }

        // Check for duplicate assignment
        var exists = await _db.UserPolicies.AnyAsync(up =>
            up.PolicyId == id &&
            up.UserId == targetUserId &&
            up.HostId == request.HostId, ct);

        if (exists)
            return Conflict(new { error = "This policy assignment already exists" });

        var assignment = new UserPolicy
        {
            Id = Guid.NewGuid(),
            UserId = targetUserId,
            PolicyId = id,
            HostId = request.HostId,
            CreatedAt = DateTime.UtcNow
        };

        _db.UserPolicies.Add(assignment);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetAssignments), new { id },
            new PolicyAssignmentDto
            {
                Id = assignment.Id,
                UserId = assignment.UserId,
                HostId = assignment.HostId,
                HostName = null, // caller can re-fetch if needed
                CreatedAt = assignment.CreatedAt
            });
    }

    [HttpDelete("{id:guid}/assignments/{assignmentId:guid}")]
    public async Task<IActionResult> DeleteAssignment(Guid id, Guid assignmentId, CancellationToken ct)
    {
        var assignment = await _db.UserPolicies.FindAsync([assignmentId], ct);
        if (assignment == null || assignment.PolicyId != id)
            return NotFound();

        // Verify policy belongs to org
        var policy = await _policyRepo.GetByIdAsync(id, ct);
        if (policy == null || policy.OrganizationId != GetOrganizationId())
            return Forbid();

        _db.UserPolicies.Remove(assignment);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- Presets ----

    [HttpGet("presets")]
    public IActionResult GetPresets()
    {
        return Ok(PolicyPresets.All);
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

public record CreatePolicyRequest(
    string Name,
    string? Description,
    List<string>? AllowedCommandPatterns,
    List<string>? DeniedCommandPatterns,
    int MaxConcurrentCommands = 5);

public record TestPolicyRequest(Guid PolicyId, string Command);

public record TestPolicyCommandRequest(string Command);

public record CreateAssignmentRequest(string? UserId, Guid? HostId);

public class PolicyAssignmentDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public Guid? HostId { get; set; }
    public string? HostName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class PolicyPresets
{
    public static readonly List<PolicyPresetDto> All =
    [
        new PolicyPresetDto
        {
            Name = "Read-Only Monitoring",
            Description = "Safe read-only commands for system monitoring and diagnostics",
            AllowedCommandPatterns = [
                "^(cat|head|tail|less|more) ",
                "^ls(\\s|$)",
                "^(df|du|free|top|htop|uptime|w|who|whoami)",
                "^(ps|pgrep) ",
                "^uname(\\s|$)",
                "^(hostname|id|date|cal)",
                "^systemctl (status|is-active|is-enabled|list-units|list-timers)",
                "^journalctl(\\s|$)",
                "^docker (ps|images|logs|inspect|stats|info|version)",
                "^(netstat|ss|ip addr|ip route|ping|traceroute|nslookup|dig|curl -s)",
                "^(lsof|lsblk|lscpu|lsmem|dmidecode)"
            ],
            DeniedCommandPatterns = [],
            MaxConcurrentCommands = 10
        },
        new PolicyPresetDto
        {
            Name = "Service Management",
            Description = "Start, stop, and restart services with destructive actions blocked",
            AllowedCommandPatterns = [
                "^(cat|head|tail|less|more) ",
                "^ls(\\s|$)",
                "^(df|du|free|top|uptime|ps|uname|hostname|id|date)",
                "^systemctl (status|start|stop|restart|reload|enable|disable|is-active|is-enabled|list-units)",
                "^journalctl(\\s|$)",
                "^docker (ps|images|logs|inspect|stats|start|stop|restart|pull)",
                "^docker-compose (ps|up|down|restart|logs|pull)"
            ],
            DeniedCommandPatterns = [
                "^rm\\s+-rf\\s+/",
                "^mkfs",
                "^dd\\s+if=",
                "^(shutdown|reboot|poweroff|halt|init\\s+[06])",
                "^(fdisk|parted|wipefs)",
                "^chmod\\s+777",
                "^chown\\s+-R"
            ],
            MaxConcurrentCommands = 5
        },
        new PolicyPresetDto
        {
            Name = "Full Access (Dangerous)",
            Description = "Allow all commands with critical destructive operations blocked. Use with caution.",
            AllowedCommandPatterns = [".*"],
            DeniedCommandPatterns = [
                "^rm\\s+-rf\\s+/$",
                "^mkfs",
                "^dd\\s+if=/dev/(zero|random|urandom)\\s+of=/dev/sd",
                "^:(){ :|:& };:",
                "^chmod\\s+-R\\s+777\\s+/",
                "^chown\\s+-R.*\\s+/$"
            ],
            MaxConcurrentCommands = 5
        }
    ];
}

public class PolicyPresetDto
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> AllowedCommandPatterns { get; set; } = [];
    public List<string> DeniedCommandPatterns { get; set; } = [];
    public int MaxConcurrentCommands { get; set; } = 5;
}
