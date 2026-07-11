# Design: Approval flow for sensitive MCP commands

Status: **proposed** (not yet implemented)

## Problem

When an external MCP client (Claude Desktop, Cursor, a headless agent) calls
`execute_command` or `write_file`, the only guardrails today are:

1. The access token — now scopeable (`mcp:read` / `mcp:execute` / `mcp:write`),
   but a scope is a static grant, not a per-action decision.
2. Command policies — regex allow/deny patterns evaluated by
   `PolicyValidationService`. Patterns are coarse: a policy that allows
   `systemctl restart .*` cannot distinguish restarting nginx in staging from
   restarting postgres in production.

There is no human-in-the-loop step. In the web UI the human *is* the loop —
they read the command before the LLM runs it. An external MCP client has no
such surface: the agent's user may never see the command at all.

## Goals

- A sensitive command requested through MCP does not execute until a human
  with authority approves it.
- The MCP client gets a well-formed, model-readable answer at every stage
  (pending / approved / denied / expired) so the agent can relay status and
  resume work.
- Approvals are auditable: who requested, who approved, what ran, when.
- Zero behavior change for non-sensitive commands and for the built-in web
  chat (where the human already sees commands before they run).

## Options considered

### Option A — MCP elicitation

The MCP spec (2025-06-18) defines `elicitation/create`: the server sends a
request *to the client* asking the end user to confirm or supply input, and
the client responds with `accept` / `decline` / `cancel`.

Pros:
- Native protocol feature; interactive clients render a real confirmation UI.
- Synchronous — the tool call blocks until the user answers, so the agent
  loop needs no polling.

Cons:
- **The requester approves their own command.** Elicitation asks the person
  driving the MCP client — the same person (or agent) that issued the
  command. That is a UX confirmation, not an authorization control. It cannot
  implement "operator requests, admin approves".
- Requires server→client requests mid-call. Over Streamable HTTP that means
  responding to the `tools/call` POST with an SSE stream, interleaving the
  elicitation request, and correlating the client's answer — a significant
  transport upgrade from our current JSON-response mode (and impossible over
  the deprecated 2024-11-05 transport that `mcp-remote` uses today).
- Client support is uneven; headless/scripted clients have no user to ask.

### Option B — Pending-approval queue (recommended)

A sensitive `tools/call` does not execute. Instead it records a
`CommandApproval` row and immediately returns a normal tool result telling
the model the command is queued, with an approval ID. Approvers see pending
requests in the web UI (SignalR push + a badge), and approve or deny. The MCP
client checks status with a `get_approval_status` tool and, once approved,
the server executes the command and stores the result on the approval record.

Pros:
- Works with **every** MCP client on **every** transport, including the
  legacy SSE transport and fully headless agents.
- The approver can be someone other than the requester — a real control.
- Naturally auditable; the approval record is the audit artifact.

Cons:
- Asynchronous: the agent must poll (or the user must tell it to re-check).
- Approval latency depends on a human noticing the queue — mitigated by
  SignalR notification in the UI (and later email/webhook).

### Decision

**Option B is the authoritative control.** Option A can be layered on later
purely as UX acceleration: when the *requesting* user is also an authorized
approver and the client advertises the `elicitation` capability, the server
may offer an inline confirm instead of parking the request in the queue. The
queue remains the fallback and the system of record.

## Design (Option B)

### What counts as "sensitive"

Reintroduce approval as a *policy* concept (a `RequireApproval` flag existed
on `Policy` and was removed in migration `20260221000000`; this brings the
idea back with pattern granularity rather than a whole-policy switch):

```csharp
public class Policy
{
    // existing fields...
    public List<string> ApprovalRequiredCommandPatterns { get; set; } = [];
}
```

Evaluation order in `PolicyValidationService` becomes:

1. Denied patterns → **deny** (unchanged)
2. Approval-required patterns → **allow, but require approval when the caller
   is an external MCP client**
3. Allowed patterns → allow (unchanged)

`PolicyValidationResult` gains `RequiresApproval` + `MatchedApprovalPattern`.
Callers that already have a human in the loop (web chat, jobs explicitly
marked pre-approved) may bypass; the MCP controller never bypasses.

### Data model

```csharp
public class CommandApproval
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid HostId { get; set; }
    public string RequestedByUserId { get; set; }   // token owner
    public string Command { get; set; }
    public string Justification { get; set; }        // supplied by the model
    public CommandApprovalStatus Status { get; set; } // Pending/Approved/Denied/Expired/Executed/Failed
    public string? DecidedByUserId { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime ExpiresAt { get; set; }           // default: RequestedAt + 1h
    public Guid? ExecutionId { get; set; }            // links to CommandExecution once run
}
```

Constraints:
- Expired requests can never be approved; a sweep (or check-on-read) flips
  `Pending` → `Expired` past `ExpiresAt`.
- The approver must be a different user than the requester **unless** the org
  setting `AllowSelfApproval` is enabled (small teams / solo operators).
- The command is executed **as recorded** — approving executes the exact
  string that was reviewed; there is no re-submission window.

### Execution semantics

Execution happens server-side when the approval is granted (approve = run),
under the *requester's* identity for policy/audit purposes, with the
approver recorded on the same row. Running at approval time (rather than
"unlock for later") keeps the reviewed command and the executed command
identical and avoids a stolen-token replay window.

### API surface (web UI)

- `GET  /api/approvals?status=pending` — list (org-scoped)
- `POST /api/approvals/{id}/approve`
- `POST /api/approvals/{id}/deny` — body: `{ reason }`
- SignalR event `approval:created` / `approval:decided` on the existing hub
  so the UI can badge the queue and live-update.

### MCP surface

- `execute_command` / `write_file`: when policy says approval is required,
  return (as a normal, non-error tool result):

  > Command requires approval before execution. Approval request
  > `9f31…` was created and is pending review. Check status with
  > `get_approval_status`. The request expires at 2026-07-10T19:00Z.

- New tool `get_approval_status` (`mcp:read`): returns status, decision
  reason, and — once executed — the captured stdout/stderr/exit code, so the
  agent can continue its task without re-running anything.
- Optional `justification` argument on `execute_command`, stored on the
  approval and shown to the approver. Prompting the model to justify
  sensitive commands materially improves reviewability.

### Audit

New `AuditEventType` values: `CommandApprovalRequested`,
`CommandApprovalGranted`, `CommandApprovalDenied`, `CommandApprovalExpired`.
The eventual execution reuses the existing command-execution audit path and
links back via `ExecutionId`.

## Future: elicitation fast-path (Option A layered on B)

Once the Streamable HTTP transport supports SSE response mode, and only when
the requester is themselves an authorized approver and the client declared
the `elicitation` capability during `initialize`:

1. `tools/call` for a sensitive command responds with an SSE stream.
2. Server sends `elicitation/create` ("Approve `systemctl restart nginx` on
   prod-web-1?").
3. `accept` → record an auto-approval (same `CommandApproval` row, requester
   = approver) and execute inline; `decline`/`cancel` → record a denial.
4. No client answer within a short timeout → fall back to the pending queue
   and return the queued-tool-result from Option B.

This keeps one system of record regardless of which path the decision took.

## Rollout

1. Migration: `CommandApprovals` table + `ApprovalRequiredCommandPatterns` on
   `Policies` (empty default = no behavior change).
2. Policy service + MCP controller changes behind the empty-pattern default.
3. Web UI queue page + SignalR notifications.
4. `get_approval_status` tool + README/tool-description updates.
5. (Later) elicitation fast-path.
