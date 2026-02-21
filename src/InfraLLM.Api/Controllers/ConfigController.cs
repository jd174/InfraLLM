using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Infrastructure.Services;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/config")]
[Authorize]
public class ConfigController : ControllerBase
{
    private readonly IConfiguration _config;

    public ConfigController(IConfiguration config)
    {
        _config = config;
    }

    /// <summary>
    /// Returns server-side feature flags that the frontend uses to
    /// conditionally enable/disable UI sections.
    /// </summary>
    [HttpGet]
    public IActionResult GetFeatures() => Ok(new
    {
        chatEnabled = AnthropicLlmService.IsProviderConfigured(_config),
        llmProvider = AnthropicLlmService.ResolveProvider(_config)
    });
}
