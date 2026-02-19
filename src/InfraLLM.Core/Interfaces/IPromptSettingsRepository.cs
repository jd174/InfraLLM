namespace InfraLLM.Core.Interfaces;

using InfraLLM.Core.Models;

public interface IPromptSettingsRepository
{
    Task<PromptSettings?> GetByUserAsync(Guid organizationId, string userId, CancellationToken ct = default);
    Task<PromptSettings> UpsertAsync(PromptSettings settings, CancellationToken ct = default);
}
