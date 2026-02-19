using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Infrastructure.Services;

public class PolicyValidationService : IPolicyService
{
    private readonly ApplicationDbContext _db;

    public PolicyValidationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PolicyValidationResult> ValidateCommandAsync(
        string userId,
        Guid hostId,
        string command,
        CancellationToken ct = default)
    {
        var hostInfo = await _db.Hosts
            .AsNoTracking()
            .Where(h => h.Id == hostId)
            .Select(h => new { h.Id, h.OrganizationId })
            .FirstOrDefaultAsync(ct);

        if (hostInfo == null)
        {
            return new PolicyValidationResult
            {
                IsAllowed = false,
                DenialReason = "Host not found"
            };
        }

        var isMember = await _db.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == hostInfo.OrganizationId, ct);

        if (!isMember)
        {
            return new PolicyValidationResult
            {
                IsAllowed = false,
                DenialReason = "User is not a member of this organization"
            };
        }

        var userPolicies = await _db.UserPolicies
            .Include(up => up.Policy)
            .Where(up => up.UserId == userId
                         && (up.HostId == null || up.HostId == hostId)
                         && up.Policy.OrganizationId == hostInfo.OrganizationId)
            .ToListAsync(ct);

        if (userPolicies.Count == 0)
        {
            return new PolicyValidationResult
            {
                IsAllowed = false,
                DenialReason = "No policies assigned to user for this host"
            };
        }

        // Check denied patterns first (deny takes precedence)
        foreach (var up in userPolicies)
        {
            foreach (var pattern in up.Policy.DeniedCommandPatterns)
            {
                if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    return new PolicyValidationResult
                    {
                        IsAllowed = false,
                        DenialReason = $"Command matches denied pattern: {pattern}",
                        MatchedPattern = pattern
                    };
                }
            }
        }

        // Check allowed patterns
        foreach (var up in userPolicies)
        {
            foreach (var pattern in up.Policy.AllowedCommandPatterns)
            {
                if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    return new PolicyValidationResult
                    {
                        IsAllowed = true,
                        RequiresApproval = up.Policy.RequireApproval,
                        MatchedPattern = pattern
                    };
                }
            }
        }

        return new PolicyValidationResult
        {
            IsAllowed = false,
            DenialReason = "Command does not match any allowed patterns"
        };
    }

    public async Task<PolicyValidationResult> TestCommandAsync(
        Guid policyId,
        string command,
        CancellationToken ct = default)
    {
        var policy = await _db.Policies.FindAsync([policyId], ct);
        if (policy == null)
        {
            return new PolicyValidationResult
            {
                IsAllowed = false,
                DenialReason = "Policy not found"
            };
        }

        foreach (var pattern in policy.DeniedCommandPatterns)
        {
            if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                return new PolicyValidationResult
                {
                    IsAllowed = false,
                    DenialReason = $"Command matches denied pattern: {pattern}",
                    MatchedPattern = pattern
                };
            }
        }

        foreach (var pattern in policy.AllowedCommandPatterns)
        {
            if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            {
                return new PolicyValidationResult
                {
                    IsAllowed = true,
                    RequiresApproval = policy.RequireApproval,
                    MatchedPattern = pattern
                };
            }
        }

        return new PolicyValidationResult
        {
            IsAllowed = false,
            DenialReason = "Command does not match any allowed patterns"
        };
    }
}
