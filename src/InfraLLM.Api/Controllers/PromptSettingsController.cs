using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PromptSettingsController : ControllerBase
{
    private readonly IPromptSettingsRepository _promptRepo;

    public PromptSettingsController(IPromptSettingsRepository promptRepo)
    {
        _promptRepo = promptRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var settings = await _promptRepo.GetByUserAsync(GetOrganizationId(), GetUserId(), ct);
        if (settings == null)
            return Ok(new PromptSettingsResponse());

        return Ok(new PromptSettingsResponse
        {
            SystemPrompt = settings.SystemPrompt,
            PersonalizationPrompt = settings.PersonalizationPrompt,
            DefaultModel = settings.DefaultModel
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePromptSettingsRequest request, CancellationToken ct)
    {
        var settings = new PromptSettings
        {
            OrganizationId = GetOrganizationId(),
            UserId = GetUserId(),
            SystemPrompt = request.SystemPrompt,
            PersonalizationPrompt = request.PersonalizationPrompt,
            DefaultModel = request.DefaultModel
        };

        var updated = await _promptRepo.UpsertAsync(settings, ct);
        return Ok(new PromptSettingsResponse
        {
            SystemPrompt = updated.SystemPrompt,
            PersonalizationPrompt = updated.PersonalizationPrompt,
            DefaultModel = updated.DefaultModel
        });
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

public class UpdatePromptSettingsRequest
{
    public string? SystemPrompt { get; set; }
    public string? PersonalizationPrompt { get; set; }
    public string? DefaultModel { get; set; }
}

public class PromptSettingsResponse
{
    public string? SystemPrompt { get; set; }
    public string? PersonalizationPrompt { get; set; }
    public string? DefaultModel { get; set; }
}
