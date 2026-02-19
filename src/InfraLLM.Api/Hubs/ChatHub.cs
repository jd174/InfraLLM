using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using InfraLLM.Infrastructure.Data;

namespace InfraLLM.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ApplicationDbContext _db;

    public ChatHub(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task SendMessage(string sessionId, string content)
    {
        var sessionGuid = ParseGuidOrThrow(sessionId, "Invalid session id");
        await EnsureSessionAccessAsync(sessionGuid);

        await Clients.Group($"session_{sessionId}").SendAsync("MessageReceived", new
        {
            sessionId,
            content,
            role = "user",
            timestamp = DateTime.UtcNow
        });
    }

    public async Task AssistantTyping(string sessionId, bool isTyping)
    {
        var sessionGuid = ParseGuidOrThrow(sessionId, "Invalid session id");
        await EnsureSessionAccessAsync(sessionGuid);

        await Clients.Group($"session_{sessionId}").SendAsync("AssistantTyping", isTyping);
    }

    public async Task JoinSession(string sessionId)
    {
        var sessionGuid = ParseGuidOrThrow(sessionId, "Invalid session id");
        await EnsureSessionAccessAsync(sessionGuid);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session_{sessionId}");
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session_{sessionId}");
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

    private async Task EnsureSessionAccessAsync(Guid sessionId)
    {
        var orgId = GetOrganizationId();
        var userId = GetUserId();

        var session = await _db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null)
            throw new HubException("Session not found");

        if (session.OrganizationId != orgId || session.UserId != userId)
            throw new HubException("Forbidden");
    }
}
