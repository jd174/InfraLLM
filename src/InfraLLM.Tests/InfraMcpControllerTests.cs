using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using InfraLLM.Api.Controllers;
using InfraLLM.Core.Enums;
using InfraLLM.Core.Interfaces;
using InfraLLM.Core.Models;

namespace InfraLLM.Tests;

public class InfraMcpControllerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // ── Streamable HTTP: initialize ─────────────────────────────────────────

    [Fact]
    public async Task Initialize_EchoesSupportedProtocolVersion_AndIssuesSessionId()
    {
        var controller = CreateController(body: InitializeBody("2025-03-26"));

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var rpc = ParseContent(result);
        Assert.Equal("2025-03-26", rpc["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(
            controller.HttpContext.Response.Headers["Mcp-Session-Id"].FirstOrDefault()));
    }

    [Fact]
    public async Task Initialize_UnknownProtocolVersion_OffersLatest()
    {
        var controller = CreateController(body: InitializeBody("1999-01-01"));

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var rpc = ParseContent(result);
        Assert.Equal("2025-06-18", rpc["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task Initialize_LegacyClientVersion_IsStillSupported()
    {
        var controller = CreateController(body: InitializeBody("2024-11-05"));

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var rpc = ParseContent(result);
        Assert.Equal("2024-11-05", rpc["result"]!["protocolVersion"]!.GetValue<string>());
    }

    // ── Streamable HTTP: protocol errors ────────────────────────────────────

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"resources/list"}""");

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var rpc = ParseContent(result);
        Assert.Equal(-32601, rpc["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task InvalidJson_ReturnsParseError()
    {
        var controller = CreateController(body: "{not json");

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
        var rpc = JsonNode.Parse(content.Content!)!.AsObject();
        Assert.Equal(-32700, rpc["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task BatchRequest_IsRejected()
    {
        var controller = CreateController(
            body: """[{"jsonrpc":"2.0","id":1,"method":"ping"}]""");

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
        var rpc = JsonNode.Parse(content.Content!)!.AsObject();
        Assert.Equal(-32600, rpc["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task Notification_ReturnsAccepted()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task UnsupportedProtocolVersionHeader_ReturnsBadRequest()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"ping"}""",
            headers: [("MCP-Protocol-Version", "1900-01-01")]);

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
    }

    [Fact]
    public async Task Ping_ReturnsEmptyResult()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var rpc = ParseContent(result);
        Assert.NotNull(rpc["result"]);
        Assert.Null(rpc["error"]);
    }

    // ── Streamable HTTP: sessions ───────────────────────────────────────────

    [Fact]
    public async Task UnknownSessionId_Returns404_SoClientReinitializes()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
            headers: [("Mcp-Session-Id", "does-not-exist")]);

        var result = await controller.StreamableHttpPost(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, content.StatusCode);
    }

    [Fact]
    public async Task SessionId_FromAnotherPrincipal_IsRejected()
    {
        // Initialize as user A to obtain a session id
        var controllerA = CreateController(body: InitializeBody("2025-06-18"));
        await controllerA.StreamableHttpPost(CancellationToken.None);
        var sessionId = controllerA.HttpContext.Response.Headers["Mcp-Session-Id"].First()!;

        // Replay the session id as a different user in a different org
        var controllerB = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
            headers: [("Mcp-Session-Id", sessionId)],
            userId: "attacker",
            orgId: Guid.NewGuid());

        var result = await controllerB.StreamableHttpPost(CancellationToken.None);

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, content.StatusCode);
    }

    [Fact]
    public async Task Delete_TerminatesOwnedSession()
    {
        var init = CreateController(body: InitializeBody("2025-06-18"));
        await init.StreamableHttpPost(CancellationToken.None);
        var sessionId = init.HttpContext.Response.Headers["Mcp-Session-Id"].First()!;

        var del = CreateController(headers: [("Mcp-Session-Id", sessionId)]);
        Assert.IsType<NoContentResult>(del.StreamableHttpDelete());

        // Second delete: session is gone
        var delAgain = CreateController(headers: [("Mcp-Session-Id", sessionId)]);
        Assert.IsType<NotFoundResult>(delAgain.StreamableHttpDelete());
    }

    [Fact]
    public void Get_ReturnsMethodNotAllowed()
    {
        var controller = CreateController();
        var result = controller.StreamableHttpGet();
        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status405MethodNotAllowed, status.StatusCode);
    }

    // ── Scopes ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToolsList_UnscopedToken_SeesAllTools()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        var rpc = ParseContent(await controller.StreamableHttpPost(CancellationToken.None));

        var tools = ToolNames(rpc);
        Assert.Contains("execute_command", tools);
        Assert.Contains("write_file", tools);
        Assert.Contains("list_hosts", tools);
    }

    [Fact]
    public async Task ToolsList_ReadScopedToken_HidesExecuteAndWriteTools()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""",
            scope: "mcp:read");

        var rpc = ParseContent(await controller.StreamableHttpPost(CancellationToken.None));

        var tools = ToolNames(rpc);
        Assert.Contains("list_hosts", tools);
        Assert.Contains("tail_logs", tools);
        Assert.DoesNotContain("execute_command", tools);
        Assert.DoesNotContain("write_file", tools);
        Assert.DoesNotContain("update_host_notes", tools);
    }

    [Fact]
    public async Task ToolCall_OutsideTokenScope_IsDeniedInResult()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"execute_command","arguments":{"host_id":"00000000-0000-0000-0000-000000000001","command":"ls"}}}""",
            scope: "mcp:read");

        var rpc = ParseContent(await controller.StreamableHttpPost(CancellationToken.None));

        Assert.True(rpc["result"]!["isError"]!.GetValue<bool>());
        var text = rpc["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("mcp:execute", text);
    }

    [Fact]
    public async Task ToolCall_WithinTokenScope_Executes()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_hosts","arguments":{}}}""",
            scope: "mcp:read");

        var rpc = ParseContent(await controller.StreamableHttpPost(CancellationToken.None));

        Assert.Null(rpc["result"]!["isError"]);
        var text = rpc["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Equal("No hosts found.", text);
    }

    [Fact]
    public async Task ToolCall_UnknownTool_ReturnsInvalidParams()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"drop_database","arguments":{}}}""");

        var rpc = ParseContent(await controller.StreamableHttpPost(CancellationToken.None));

        Assert.Equal(-32602, rpc["error"]!["code"]!.GetValue<int>());
    }

    // ── Legacy stateless mode (/mcp/messages) ───────────────────────────────

    [Fact]
    public async Task LegacyMessages_StatelessRequest_ReturnsJsonBody()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":"abc","method":"tools/list"}""");

        var result = await controller.Messages(session: null, CancellationToken.None);

        var rpc = ParseContent(result);
        Assert.Equal("abc", rpc["id"]!.GetValue<string>());
        Assert.NotNull(rpc["result"]!["tools"]);
    }

    [Fact]
    public async Task LegacyMessages_Notification_ReturnsAccepted()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        var result = await controller.Messages(session: null, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task LegacyMessages_UnknownSseSession_Returns404()
    {
        var controller = CreateController(
            body: """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        var result = await controller.Messages(session: "not-a-real-session", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string InitializeBody(string protocolVersion) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = 0,
        ["method"] = "initialize",
        ["params"] = new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" }
        }
    }.ToJsonString();

    private static JsonObject ParseContent(IActionResult result)
    {
        var content = Assert.IsType<ContentResult>(result);
        return JsonNode.Parse(content.Content!)!.AsObject();
    }

    private static List<string> ToolNames(JsonObject rpc) =>
        rpc["result"]!["tools"]!.AsArray()
            .Select(t => t!["name"]!.GetValue<string>())
            .ToList();

    private static InfraMcpController CreateController(
        string? body = null,
        string? scope = null,
        (string Key, string Value)[]? headers = null,
        string userId = "user-1",
        Guid? orgId = null)
    {
        var controller = new InfraMcpController(
            new FakeHostRepository(),
            new FakeHostNoteRepository(),
            new FakePolicyRepository(),
            new FakeAuditRepository(),
            new FakeCommandExecutor(),
            new FakeSshConnectionPool());

        var http = new DefaultHttpContext();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("org_id", (orgId ?? OrgId).ToString()),
            new("auth_method", "access_token"),
        };
        if (scope != null)
            claims.Add(new Claim("scope", scope));
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "AccessToken"));

        if (body != null)
        {
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            http.Request.ContentType = "application/json";
        }

        if (headers != null)
        {
            foreach (var (key, value) in headers)
                http.Request.Headers[key] = value;
        }

        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    // ── Fakes ───────────────────────────────────────────────────────────────

    private sealed class FakeHostRepository : IHostRepository
    {
        public Task<Host?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Host?>(null);
        public Task<List<Host>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => Task.FromResult(new List<Host>());
        public Task<List<Host>> GetByEnvironmentAsync(Guid organizationId, string environment, CancellationToken ct = default) => Task.FromResult(new List<Host>());
        public Task<List<Guid>> GetOrganizationIdsWithHostsAsync(CancellationToken ct = default) => Task.FromResult(new List<Guid>());
        public Task<Host> CreateAsync(Host host, CancellationToken ct = default) => Task.FromResult(host);
        public Task<Host> UpdateAsync(Host host, CancellationToken ct = default) => Task.FromResult(host);
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeHostNoteRepository : IHostNoteRepository
    {
        public Task<List<HostNote>> GetByHostIdsAsync(Guid organizationId, List<Guid> hostIds, CancellationToken ct = default) => Task.FromResult(new List<HostNote>());
        public Task<HostNote?> GetByHostIdAsync(Guid organizationId, Guid hostId, CancellationToken ct = default) => Task.FromResult<HostNote?>(null);
        public Task<HostNote> UpsertAsync(HostNote note, CancellationToken ct = default) => Task.FromResult(note);
    }

    private sealed class FakePolicyRepository : IPolicyRepository
    {
        public Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Policy?>(null);
        public Task<List<Policy>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) => Task.FromResult(new List<Policy>());
        public Task<Policy> CreateAsync(Policy policy, CancellationToken ct = default) => Task.FromResult(policy);
        public Task<Policy> UpdateAsync(Policy policy, CancellationToken ct = default) => Task.FromResult(policy);
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAuditRepository : IAuditRepository
    {
        public Task<AuditLog> CreateAsync(AuditLog log, CancellationToken ct = default) => Task.FromResult(log);
        public Task<AuditLog?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<AuditLog?>(null);
        public Task<(List<AuditLog> Items, int TotalCount)> SearchAsync(
            Guid organizationId,
            string? userId = null,
            Guid? hostId = null,
            AuditEventType? eventType = null,
            DateTime? from = null,
            DateTime? to = null,
            string? commandSearch = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken ct = default)
            => Task.FromResult((new List<AuditLog>(), 0));
    }

    private sealed class FakeCommandExecutor : ICommandExecutor
    {
        public Task<CommandResult> ExecuteAsync(string userId, Guid hostId, string command, bool dryRun = false, CancellationToken ct = default)
            => Task.FromResult(new CommandResult { Command = command, ExitCode = 0, WasDryRun = dryRun });

        public async IAsyncEnumerable<string> StreamCommandOutputAsync(string userId, Guid hostId, string command, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeSshConnectionPool : ISshConnectionPool
    {
        public Task<object> GetConnectionAsync(Guid hostId, CancellationToken ct = default) => Task.FromResult<object>(new());
        public Task ReleaseConnectionAsync(Guid hostId, object client) => Task.CompletedTask;
        public Task<bool> TestConnectionAsync(Guid hostId, CancellationToken ct = default) => Task.FromResult(true);
        public Task InvalidateHostAsync(Guid hostId) => Task.CompletedTask;
    }
}
