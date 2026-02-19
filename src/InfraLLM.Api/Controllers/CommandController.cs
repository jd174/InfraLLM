using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Interfaces;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CommandController : ControllerBase
{
    private readonly ICommandExecutor _executor;

    public CommandController(ICommandExecutor executor)
    {
        _executor = executor;
    }

    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] ExecuteCommandRequest request, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(GetUserId(), request.HostId, request.Command, false, ct);
        return Ok(result);
    }

    [HttpPost("execute/dry-run")]
    public async Task<IActionResult> DryRun([FromBody] ExecuteCommandRequest request, CancellationToken ct)
    {
        var result = await _executor.ExecuteAsync(GetUserId(), request.HostId, request.Command, true, ct);
        return Ok(result);
    }

    private string GetUserId()
        => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? throw new UnauthorizedAccessException();
}

public record ExecuteCommandRequest(Guid HostId, string Command, string? WorkingDirectory = null);
