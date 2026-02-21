namespace InfraLLM.Core.Interfaces;

public class PolicyValidationResult
{
    public bool IsAllowed { get; set; }
    public string? DenialReason { get; set; }
    public string? MatchedPattern { get; set; }
}

public interface IPolicyService
{
    Task<PolicyValidationResult> ValidateCommandAsync(
        string userId,
        Guid hostId,
        string command,
        CancellationToken ct = default);

    Task<PolicyValidationResult> TestCommandAsync(
        Guid policyId,
        string command,
        CancellationToken ct = default);
}
