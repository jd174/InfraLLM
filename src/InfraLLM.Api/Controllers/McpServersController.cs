using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using InfraLLM.Infrastructure.Services.Mcp;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/mcp-servers")]
[Authorize]
public class McpServersController : ControllerBase
{
    private readonly IMcpServerRepository _mcpServerRepo;
    private readonly IMcpClientFactory _mcpClientFactory;
    private readonly StdioMcpClientCache _stdioCache;
    private readonly ICredentialEncryptionService _encryptionService;

    public McpServersController(
        IMcpServerRepository mcpServerRepo,
        IMcpClientFactory mcpClientFactory,
        StdioMcpClientCache stdioCache,
        ICredentialEncryptionService encryptionService)
    {
        _mcpServerRepo = mcpServerRepo;
        _mcpClientFactory = mcpClientFactory;
        _stdioCache = stdioCache;
        _encryptionService = encryptionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var orgId = GetOrganizationId();
        var servers = await _mcpServerRepo.GetByOrganizationAsync(orgId, ct);
        return Ok(servers.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var server = await _mcpServerRepo.GetByIdAsync(id, ct);
        if (server == null) return NotFound();
        if (server.OrganizationId != GetOrganizationId()) return Forbid();
        return Ok(ToResponse(server));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMcpServerRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Server name is required" });

        if (request.TransportType == McpTransportType.Http && string.IsNullOrWhiteSpace(request.BaseUrl))
            return BadRequest(new { error = "BaseUrl is required for HTTP transport" });

        if (request.TransportType == McpTransportType.Stdio && string.IsNullOrWhiteSpace(request.Command))
            return BadRequest(new { error = "Command is required for Stdio transport" });

        var server = new McpServer
        {
            OrganizationId = GetOrganizationId(),
            Name = request.Name,
            Description = request.Description,
            TransportType = request.TransportType,
            BaseUrl = request.BaseUrl,
            Command = request.Command,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            EnvironmentVariables = request.EnvironmentVariables ?? [],
            IsEnabled = request.IsEnabled ?? true,
            CreatedBy = GetUserId()
        };

        // Encrypt API key if provided
        if (!string.IsNullOrEmpty(request.ApiKey))
            server.ApiKeyEncrypted = _encryptionService.Encrypt(request.ApiKey);

        var created = await _mcpServerRepo.CreateAsync(server, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToResponse(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMcpServerRequest request, CancellationToken ct)
    {
        var server = await _mcpServerRepo.GetByIdAsync(id, ct);
        if (server == null) return NotFound();
        if (server.OrganizationId != GetOrganizationId()) return Forbid();

        server.Name = request.Name ?? server.Name;
        server.Description = request.Description ?? server.Description;
        server.BaseUrl = request.BaseUrl ?? server.BaseUrl;
        server.Command = request.Command ?? server.Command;
        server.Arguments = request.Arguments ?? server.Arguments;
        server.WorkingDirectory = request.WorkingDirectory ?? server.WorkingDirectory;
        server.EnvironmentVariables = request.EnvironmentVariables ?? server.EnvironmentVariables;
        server.IsEnabled = request.IsEnabled ?? server.IsEnabled;

        // Update encrypted API key if a new one was provided
        if (request.ApiKey != null)
        {
            server.ApiKeyEncrypted = string.IsNullOrEmpty(request.ApiKey)
                ? null
                : _encryptionService.Encrypt(request.ApiKey);
        }

        var updated = await _mcpServerRepo.UpdateAsync(server, ct);

        // Invalidate cached stdio process so next use picks up the new config
        if (server.TransportType == McpTransportType.Stdio)
            await _stdioCache.InvalidateAsync(id);

        return Ok(ToResponse(updated));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var server = await _mcpServerRepo.GetByIdAsync(id, ct);
        if (server == null) return NotFound();
        if (server.OrganizationId != GetOrganizationId()) return Forbid();

        // Dispose cached process before deleting from DB
        if (server.TransportType == McpTransportType.Stdio)
            await _stdioCache.InvalidateAsync(id);

        await _mcpServerRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Test connectivity to an MCP server and list its available tools.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var server = await _mcpServerRepo.GetByIdAsync(id, ct);
        if (server == null) return NotFound();
        if (server.OrganizationId != GetOrganizationId()) return Forbid();

        try
        {
            await using var client = await _mcpClientFactory.CreateAsync(server, ct);
            var tools = await client.ListToolsAsync(ct);
            return Ok(new
            {
                success = true,
                toolCount = tools.Count,
                tools = tools.Select(t => new { t.Name, t.Description })
            });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// List currently available tools from an MCP server (live discovery).
    /// Stdio servers reuse the cached process; HTTP servers get a fresh client.
    /// </summary>
    [HttpGet("{id:guid}/tools")]
    public async Task<IActionResult> GetTools(Guid id, CancellationToken ct)
    {
        var server = await _mcpServerRepo.GetByIdAsync(id, ct);
        if (server == null) return NotFound();
        if (server.OrganizationId != GetOrganizationId()) return Forbid();

        try
        {
            IReadOnlyList<McpTool> tools;

            if (server.TransportType == McpTransportType.Stdio)
            {
                // Use cached process — do NOT dispose it
                var client = await _stdioCache.GetOrCreateAsync(server, ct);
                tools = await client.ListToolsAsync(ct);
            }
            else
            {
                await using var client = await _mcpClientFactory.CreateAsync(server, ct);
                tools = await client.ListToolsAsync(ct);
            }

            return Ok(tools.Select(t => new { t.Name, t.Description }));
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = $"Failed to reach MCP server: {ex.Message}" });
        }
    }

    /// <summary>
    /// Returns the most recent log entries captured from a running stdio MCP server process
    /// (stderr output, lifecycle events, RPC errors). Returns an empty array for HTTP servers
    /// or servers that have never been started.
    /// </summary>
    [HttpGet("{id:guid}/logs")]
    public async Task<IActionResult> GetLogs(Guid id, [FromQuery] int count = 100, CancellationToken ct = default)
    {
        var server = await _mcpServerRepo.GetByIdAsync(id, ct);
        if (server == null) return NotFound();
        if (server.OrganizationId != GetOrganizationId()) return Forbid();

        var entries = _stdioCache.GetLogs(id, count);
        return Ok(entries.Select(e => new
        {
            timestamp = e.Timestamp,
            level = e.Level,
            message = e.Message
        }));
    }

    private Guid GetOrganizationId()
    {
        var claim = User.FindFirst("org_id")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new UnauthorizedAccessException("No organization context");
    }

    private string GetUserId()
        => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? throw new UnauthorizedAccessException();

    /// <summary>
    /// Returns a safe response DTO (excludes encrypted API key).
    /// </summary>
    private static McpServerResponse ToResponse(McpServer s) => new(
        s.Id,
        s.Name,
        s.Description,
        s.TransportType,
        s.BaseUrl,
        s.Command,
        s.Arguments,
        s.WorkingDirectory,
        s.EnvironmentVariables,
        s.IsEnabled,
        s.ApiKeyEncrypted != null,  // hasApiKey: don't return the actual key
        s.CreatedAt,
        s.CreatedBy);
}

public record McpServerResponse(
    Guid Id,
    string Name,
    string? Description,
    McpTransportType TransportType,
    string? BaseUrl,
    string? Command,
    string? Arguments,
    string? WorkingDirectory,
    Dictionary<string, string> EnvironmentVariables,
    bool IsEnabled,
    bool HasApiKey,
    DateTime CreatedAt,
    string CreatedBy);

public record CreateMcpServerRequest(
    string Name,
    string? Description,
    McpTransportType TransportType,
    string? BaseUrl,
    string? ApiKey,
    string? Command,
    string? Arguments,
    string? WorkingDirectory,
    Dictionary<string, string>? EnvironmentVariables,
    bool? IsEnabled);

public record UpdateMcpServerRequest(
    string? Name,
    string? Description,
    string? BaseUrl,
    string? ApiKey,
    string? Command,
    string? Arguments,
    string? WorkingDirectory,
    Dictionary<string, string>? EnvironmentVariables,
    bool? IsEnabled);
