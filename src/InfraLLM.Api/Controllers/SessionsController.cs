using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using InfraLLM.Api.Hubs;
using InfraLLM.Api.Services;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;
using HostModel = InfraLLM.Core.Models.Host;

namespace InfraLLM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly ISessionRepository _sessionRepo;
    private readonly ILlmService _llmService;
    private readonly IHostRepository _hostRepo;
    private readonly IPolicyRepository _policyRepo;
    private readonly IPromptSettingsRepository _promptRepo;
    private readonly IHostNoteRepository _hostNoteRepo;
    private readonly IHubContext<ChatHub> _chatHub;
    private readonly IChatTaskManager _chatTasks;

    public SessionsController(
        ISessionRepository sessionRepo,
        ILlmService llmService,
        IHostRepository hostRepo,
        IPolicyRepository policyRepo,
        IPromptSettingsRepository promptRepo,
        IHostNoteRepository hostNoteRepo,
        IHubContext<ChatHub> chatHub,
        IChatTaskManager chatTasks)
    {
        _sessionRepo = sessionRepo;
        _llmService = llmService;
        _hostRepo = hostRepo;
        _policyRepo = policyRepo;
        _promptRepo = promptRepo;
        _hostNoteRepo = hostNoteRepo;
        _chatHub = chatHub;
        _chatTasks = chatTasks;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var sessions = await _sessionRepo.GetByUserAsync(GetOrganizationId(), GetUserId(), ct);
        sessions = sessions.Where(s => !s.IsJobRunSession).ToList();
        return Ok(sessions);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSessionRequest? request, CancellationToken ct)
    {
        if (request?.HostIds != null && request.HostIds.Count > 0)
        {
            var orgId = GetOrganizationId();
            var hosts = await _hostRepo.GetByOrganizationAsync(orgId, ct);
            var hostIds = new HashSet<Guid>(hosts.Select(h => h.Id));
            if (request.HostIds.Any(id => !hostIds.Contains(id)))
                return BadRequest(new { error = "One or more hosts are invalid" });
        }

        var session = new Session
        {
            OrganizationId = GetOrganizationId(),
            UserId = GetUserId(),
            Title = request?.Title ?? $"Session {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            HostIds = request?.HostIds ?? [],
            LastMessageAt = DateTime.UtcNow
        };

        var created = await _sessionRepo.CreateAsync(session, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var session = await _sessionRepo.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        
        // Ensure user can only access their own sessions
        if (session.OrganizationId != GetOrganizationId() || session.UserId != GetUserId())
            return Forbid();
            
        return Ok(session);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var session = await _sessionRepo.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        
        // Ensure user can only delete their own sessions
        if (session.OrganizationId != GetOrganizationId() || session.UserId != GetUserId())
            return Forbid();
            
        await _sessionRepo.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> GetMessages(Guid id, CancellationToken ct)
    {
        var session = await _sessionRepo.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        if (session.OrganizationId != GetOrganizationId() || session.UserId != GetUserId())
            return Forbid();
            
        var messages = await _sessionRepo.GetMessagesAsync(id, ct);
        return Ok(messages);
    }

    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { error = "Message content is required" });
            
        // Verify session exists and user has access
        var session = await _sessionRepo.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        if (session.OrganizationId != GetOrganizationId() || session.UserId != GetUserId())
            return Forbid();

        // Fetch context for LLM: hosts, policies, and conversation history
        var orgId = GetOrganizationId();
        var userId = GetUserId();
        var hosts = await _hostRepo.GetByOrganizationAsync(orgId, ct);
        if (request.HostIds != null)
        {
            if (request.HostIds.Count > 0)
            {
                var hostIds = new HashSet<Guid>(hosts.Select(h => h.Id));
                if (request.HostIds.Any(id => !hostIds.Contains(id)))
                    return BadRequest(new { error = "One or more hosts are invalid" });

                var allowed = new HashSet<Guid>(request.HostIds);
                hosts = hosts.Where(h => allowed.Contains(h.Id)).ToList();
            }

            var currentIds = new HashSet<Guid>(session.HostIds);
            var requestedIds = new HashSet<Guid>(request.HostIds);
            if (!currentIds.SetEquals(requestedIds))
            {
                session.HostIds = request.HostIds;
                await _sessionRepo.UpdateAsync(session, ct);
            }
        }
        else if (session.HostIds.Count > 0)
        {
            var allowed = new HashSet<Guid>(session.HostIds);
            hosts = hosts.Where(h => allowed.Contains(h.Id)).ToList();
        }
        var policies = await _policyRepo.GetByOrganizationAsync(orgId, ct);
        var conversationHistory = await _sessionRepo.GetMessagesAsync(id, ct);
        var promptSettings = await _promptRepo.GetByUserAsync(orgId, userId, ct);
        var hostNotes = await _hostNoteRepo.GetByHostIdsAsync(orgId, hosts.Select(h => h.Id).ToList(), ct);
        var systemPrompt = MergeSystemPrompts(promptSettings?.SystemPrompt, BuildHostNotesContext(hosts, hostNotes));
        var selectedModel = request.Model ?? promptSettings?.DefaultModel;

        // Save user message
        var userMessage = new Message
        {
            SessionId = id,
            Role = "user",
            Content = request.Content
        };
        var savedUserMessage = await _sessionRepo.AddMessageAsync(userMessage, ct);

        session.LastMessageAt = savedUserMessage.CreatedAt;
        await _sessionRepo.UpdateAsync(session, ct);
        await _chatHub.Clients.Group($"user_{GetUserId()}").SendAsync("SessionUpdated", new
        {
            sessionId = session.Id,
            title = session.Title,
            lastMessageAt = session.LastMessageAt
        }, ct);

        // Create placeholder assistant message for streaming
        var assistantMessage = new Message
        {
            SessionId = id,
            Role = "assistant",
            Content = string.Empty
        };
        var savedAssistantMessage = await _sessionRepo.AddMessageAsync(assistantMessage, ct);

        await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantStarted", new
        {
            sessionId = id,
            messageId = savedAssistantMessage.Id,
            createdAt = savedAssistantMessage.CreatedAt
        }, ct);

        await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantTyping", true, ct);

        var llmToken = _chatTasks.Begin(id, userId, ct);

        try
        {
            // Send to LLM with full context (streaming)
            var response = await _llmService.SendMessageStreamAsync(
                userId,
                id.ToString(),
                orgId,
                request.Content,
                hosts,
                policies,
                conversationHistory,
                systemPrompt,
                promptSettings?.PersonalizationPrompt,
                selectedModel,
                async delta =>
                {
                    await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantDelta", new
                    {
                        sessionId = id,
                        messageId = savedAssistantMessage.Id,
                        delta
                    }, ct);
                },
                async status =>
                {
                    await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantStatus", new
                    {
                        sessionId = id,
                        messageId = savedAssistantMessage.Id,
                        status
                    }, ct);
                },
                llmToken);

            savedAssistantMessage.Content = response.Content;
            savedAssistantMessage.TokensUsed = response.TokensUsed;
            await _sessionRepo.UpdateMessageAsync(savedAssistantMessage, ct);

            session.LastMessageAt = savedAssistantMessage.CreatedAt;
            await _sessionRepo.UpdateAsync(session, ct);

            await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantCompleted", new
            {
                sessionId = id,
                messageId = savedAssistantMessage.Id,
                content = response.Content,
                tokensUsed = response.TokensUsed,
                cost = response.Cost
            }, ct);

            await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantTyping", false, ct);

            if (IsDefaultSessionTitle(session.Title))
            {
                var titleHistory = new List<Message>(conversationHistory.Count + 2);
                titleHistory.AddRange(conversationHistory);
                titleHistory.Add(savedUserMessage);
                titleHistory.Add(savedAssistantMessage);

                var generatedTitle = await _llmService.GenerateSessionTitleAsync(userId, titleHistory, ct);
                if (!string.IsNullOrWhiteSpace(generatedTitle) && !IsDefaultSessionTitle(generatedTitle))
                {
                    session.Title = generatedTitle;
                    await _sessionRepo.UpdateAsync(session, ct);
                }
            }

            await _chatHub.Clients.Group($"user_{userId}").SendAsync("SessionUpdated", new
            {
                sessionId = session.Id,
                title = session.Title,
                lastMessageAt = session.LastMessageAt
            }, ct);

            // Return both messages so the frontend can update properly
            return Ok(new 
            { 
                userMessage = savedUserMessage, 
                assistantMessage = savedAssistantMessage,
                tokensUsed = response.TokensUsed,
                cost = response.Cost,
                streamed = true
            });
        }
        catch (OperationCanceledException)
        {
            savedAssistantMessage.Content = "Cancelled";
            savedAssistantMessage.TokensUsed = 0;
            await _sessionRepo.UpdateMessageAsync(savedAssistantMessage, CancellationToken.None);

            await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantStatus", new
            {
                sessionId = id,
                messageId = savedAssistantMessage.Id,
                status = "Cancelled"
            }, CancellationToken.None);

            await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantCompleted", new
            {
                sessionId = id,
                messageId = savedAssistantMessage.Id,
                content = savedAssistantMessage.Content,
                tokensUsed = 0,
                cost = 0
            }, CancellationToken.None);

            await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantTyping", false, CancellationToken.None);

            return Ok(new
            {
                userMessage = savedUserMessage,
                assistantMessage = savedAssistantMessage,
                tokensUsed = 0,
                cost = 0,
                streamed = true,
                cancelled = true
            });
        }
        finally
        {
            _chatTasks.Complete(id, userId);
        }
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var session = await _sessionRepo.GetByIdAsync(id, ct);
        if (session == null) return NotFound();
        if (session.OrganizationId != GetOrganizationId() || session.UserId != GetUserId())
            return Forbid();

        var cancelled = _chatTasks.Cancel(id, GetUserId());

        if (cancelled)
        {
            await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantStatus", new
            {
                sessionId = id,
                messageId = string.Empty,
                status = "Cancelled"
            }, ct);

            await _chatHub.Clients.Group($"session_{id}").SendAsync("AssistantTyping", false, ct);
        }

        return Ok(new { cancelled });
    }


    private Guid GetOrganizationId()
    {
        var claim = User.FindFirst("org_id")?.Value;
        return claim != null ? Guid.Parse(claim) : throw new UnauthorizedAccessException("No organization context");
    }

    private string GetUserId()
        => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
           ?? throw new UnauthorizedAccessException();

    private static bool IsDefaultSessionTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        return title.StartsWith("Session ", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildHostNotesContext(List<HostModel> hosts, List<HostNote> notes)
    {
        if (hosts.Count == 0) return null;
        var notesByHost = notes.ToDictionary(n => n.HostId, n => n.Content);
        var lines = new List<string> { "Host notes:" };
        foreach (var host in hosts)
        {
            if (!notesByHost.TryGetValue(host.Id, out var content) || string.IsNullOrWhiteSpace(content))
                continue;

            lines.Add($"- {host.Name} ({host.Type}) {host.Hostname}:{host.Port}");
            lines.Add($"  Notes: {content.Replace("\n", " ").Trim()}");
        }

        return lines.Count > 1 ? string.Join("\n", lines) : null;
    }

    private static string? MergeSystemPrompts(string? userPrompt, string? hostNotesPrompt)
    {
        if (string.IsNullOrWhiteSpace(hostNotesPrompt)) return userPrompt;
        if (string.IsNullOrWhiteSpace(userPrompt)) return hostNotesPrompt;
        return $"{hostNotesPrompt}\n\n{userPrompt}";
    }
}

public class CreateSessionRequest
{
    public string? Title { get; set; }
    public List<Guid>? HostIds { get; set; }
}

public class SendMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public List<Guid>? HostIds { get; set; }
    public string? Model { get; set; }
}
