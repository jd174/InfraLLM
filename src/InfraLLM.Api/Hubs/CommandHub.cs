using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Api.Hubs;

[Authorize]
public class CommandHub : Hub
{
    private readonly ApplicationDbContext _db;

    public CommandHub(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task StreamCommandOutput(string executionId, string output)
    {
        await Clients.Group(executionId).SendAsync("ReceiveOutput", output);
    }

    public async Task CommandCompleted(string executionId, int exitCode)
    {
        await Clients.Group(executionId).SendAsync("CommandCompleted", exitCode);
    }

    public async Task CommandFailed(string executionId, string error)
    {
        await Clients.Group(executionId).SendAsync("CommandFailed", error);
    }

    public async Task JoinExecution(string executionId)
    {
        var execGuid = ParseGuidOrThrow(executionId, "Invalid execution id");
        await EnsureExecutionAccessAsync(execGuid);
        await Groups.AddToGroupAsync(Context.ConnectionId, executionId);
    }

    public async Task LeaveExecution(string executionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, executionId);
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId != null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }
        await base.OnConnectedAsync();
    }

    private static Guid ParseGuidOrThrow(string value, string message)
        => Guid.TryParse(value, out var guid) ? guid : throw new HubException(message);

    private Guid GetOrganizationId()
    {
        var claim = Context.User?.FindFirst("org_id")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new HubException("No organization context");
    }

    private string GetUserId()
        => Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? throw new HubException("Not authenticated");

    private async Task EnsureExecutionAccessAsync(Guid executionId)
    {
        var orgId = GetOrganizationId();
        var userId = GetUserId();

        var execution = await _db.CommandExecutions
            .AsNoTracking()
            .Include(e => e.Host)
            .FirstOrDefaultAsync(e => e.Id == executionId);

        if (execution == null)
            throw new HubException("Execution not found");

        if (execution.UserId != userId)
            throw new HubException("Forbidden");

        if (execution.Host.OrganizationId != orgId)
            throw new HubException("Forbidden");
    }
}
