using Microsoft.EntityFrameworkCore;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Infrastructure.Data.Repositories;

public class PromptSettingsRepository : IPromptSettingsRepository
{
    private readonly ApplicationDbContext _db;

    public PromptSettingsRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PromptSettings?> GetByUserAsync(Guid organizationId, string userId, CancellationToken ct = default)
        => await _db.PromptSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId, ct);

    public async Task<PromptSettings> UpsertAsync(PromptSettings settings, CancellationToken ct = default)
    {
        var existing = await _db.PromptSettings
            .FirstOrDefaultAsync(p => p.OrganizationId == settings.OrganizationId && p.UserId == settings.UserId, ct);

        if (existing == null)
        {
            settings.Id = Guid.NewGuid();
            settings.CreatedAt = DateTime.UtcNow;
            settings.UpdatedAt = DateTime.UtcNow;
            _db.PromptSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
            return settings;
        }

        existing.SystemPrompt = settings.SystemPrompt;
        existing.PersonalizationPrompt = settings.PersonalizationPrompt;
        existing.DefaultModel = settings.DefaultModel;
        existing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return existing;
    }
}
