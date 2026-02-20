"use client";

import { useState, useEffect, useCallback } from "react";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { Card, CardContent, CardHeader } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { Alert } from "@/components/ui/Alert";
import { PlusIcon, XIcon, TrashIcon, CopyIcon, CheckIcon } from "@/components/ui/Icons";

interface AccessToken {
  id: string;
  name: string;
  createdAt: string;
  expiresAt: string | null;
  lastUsedAt: string | null;
  isActive: boolean;
}

interface CreatedToken {
  id: string;
  name: string;
  token: string;
  createdAt: string;
  expiresAt: string | null;
}

function useAccessTokens() {
  const [tokens, setTokens] = useState<AccessToken[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await api.get<AccessToken[]>("/api/access-tokens");
      setTokens(data);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load tokens");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const create = async (name: string, expiresAt: string | null): Promise<CreatedToken> => {
    const result = await api.post<CreatedToken>("/api/access-tokens", { name, expiresAt });
    await load();
    return result;
  };

  const revoke = async (id: string) => {
    await api.delete(`/api/access-tokens/${id}`);
    setTokens((prev) => prev.filter((t) => t.id !== id));
  };

  return { tokens, loading, error, create, revoke };
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // ignore
    }
  };

  return (
    <button
      onClick={handleCopy}
      className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition"
      title="Copy to clipboard"
    >
      {copied ? <CheckIcon size={14} /> : <CopyIcon size={14} />}
    </button>
  );
}

function TokenRevealCard({ token, onDismiss }: { token: CreatedToken; onDismiss: () => void }) {
  return (
    <div className="rounded-lg border border-yellow-500/30 bg-yellow-500/5 p-4 mb-6">
      <div className="flex items-start justify-between mb-2">
        <div>
          <p className="text-sm font-semibold text-foreground">Token created: {token.name}</p>
          <p className="text-xs text-yellow-400 mt-0.5">
            Copy this token now — it will not be shown again.
          </p>
        </div>
        <button
          onClick={onDismiss}
          className="text-muted-foreground hover:text-foreground transition"
        >
          <XIcon size={16} />
        </button>
      </div>
      <div className="flex items-center gap-2 mt-3 p-2.5 bg-background rounded border border-border">
        <code className="text-xs font-mono text-foreground break-all flex-1 select-all">
          {token.token}
        </code>
        <CopyButton text={token.token} />
      </div>
    </div>
  );
}

function McpInstructions({ tokens }: { tokens: AccessToken[] }) {
  const [copied, setCopied] = useState(false);
  const activeToken = tokens.find((t) => t.isActive);

  const baseUrl =
    typeof window !== "undefined"
      ? `${window.location.protocol}//${window.location.host}`
      : "https://your-infra-llm-instance";

  const mcpUrl = `${baseUrl}/mcp/messages`;

  const configSnippet = JSON.stringify(
    {
      mcpServers: {
        infra: {
          command: "npx",
          args: ["-y", "@modelcontextprotocol/client-http", mcpUrl],
          env: {
            API_KEY: activeToken ? "<your-access-token>" : "<create an access token first>",
          },
        },
      },
    },
    null,
    2
  );

  const handleCopyUrl = async () => {
    try {
      await navigator.clipboard.writeText(mcpUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // ignore
    }
  };

  return (
    <Card>
      <CardHeader>
        <h2 className="text-sm font-semibold text-foreground">Use InfraLLM as an MCP server</h2>
        <p className="text-xs text-muted-foreground mt-0.5">
          Connect any MCP-compatible client (Claude Desktop, Cursor, etc.) to InfraLLM to run commands,
          inspect hosts, and query audit logs.
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        <div>
          <Label className="mb-1.5">MCP endpoint</Label>
          <div className="flex items-center gap-2 p-2.5 bg-muted/50 rounded border border-border">
            <code className="text-xs font-mono text-foreground flex-1 break-all">{mcpUrl}</code>
            <button
              onClick={handleCopyUrl}
              className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition shrink-0"
              title="Copy URL"
            >
              {copied ? <CheckIcon size={14} /> : <CopyIcon size={14} />}
            </button>
          </div>
        </div>

        <div>
          <Label className="mb-1.5">Authentication</Label>
          <p className="text-xs text-muted-foreground mb-2">
            Pass your access token using one of these methods:
          </p>
          <ul className="space-y-1 text-xs text-muted-foreground list-disc list-inside">
            <li>
              Header: <code className="font-mono bg-muted px-1 rounded">Authorization: Bearer infra_...</code>
            </li>
            <li>
              Header: <code className="font-mono bg-muted px-1 rounded">X-API-Key: infra_...</code>
            </li>
            <li>
              Query: <code className="font-mono bg-muted px-1 rounded">?api_key=infra_...</code>
            </li>
          </ul>
        </div>

        <div>
          <Label className="mb-1.5">Available tools</Label>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-1.5">
            {[
              { name: "list_hosts", desc: "List all managed hosts" },
              { name: "get_host_details", desc: "Get host info + notes" },
              { name: "execute_command", desc: "Run SSH commands (policy-checked)" },
              { name: "test_host_connection", desc: "Test SSH connectivity" },
              { name: "list_policies", desc: "List command policies" },
              { name: "get_audit_logs", desc: "Query audit log entries" },
            ].map((tool) => (
              <div
                key={tool.name}
                className="flex items-start gap-2 p-2 rounded border border-border bg-muted/30"
              >
                <code className="text-xs font-mono text-foreground shrink-0">{tool.name}</code>
                <span className="text-xs text-muted-foreground">{tool.desc}</span>
              </div>
            ))}
          </div>
        </div>

        <div>
          <div className="flex items-center justify-between mb-1.5">
            <Label>Add InfraLLM to this app as an MCP server</Label>
          </div>
          <p className="text-xs text-muted-foreground mb-2">
            You can also add InfraLLM as an MCP server within this app itself — go to{" "}
            <a href="/mcp-servers" className="underline hover:text-foreground">
              MCP Servers
            </a>{" "}
            and create an HTTP server with the URL above and your access token as the API key.
          </p>
        </div>
      </CardContent>
    </Card>
  );
}

export default function AccessTokensPage() {
  const { tokens, loading, error, create, revoke } = useAccessTokens();
  const [showCreate, setShowCreate] = useState(false);
  const [name, setName] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [newToken, setNewToken] = useState<CreatedToken | null>(null);
  const [revoking, setRevoking] = useState<string | null>(null);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    setCreating(true);
    setCreateError(null);
    try {
      const result = await create(
        name.trim(),
        expiresAt ? new Date(expiresAt).toISOString() : null
      );
      setNewToken(result);
      setShowCreate(false);
      setName("");
      setExpiresAt("");
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : "Failed to create token");
    } finally {
      setCreating(false);
    }
  };

  const handleRevoke = async (id: string) => {
    setRevoking(id);
    try {
      await revoke(id);
    } finally {
      setRevoking(null);
    }
  };

  const formatDate = (str: string | null) => {
    if (!str) return "—";
    return new Date(str).toLocaleDateString("en-US", {
      month: "short",
      day: "numeric",
      year: "numeric",
    });
  };

  const isExpired = (expiresAt: string | null) =>
    expiresAt != null && new Date(expiresAt) < new Date();

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading access tokens...</p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-6 max-w-3xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold text-foreground">Access Tokens</h1>
          <p className="text-sm text-muted-foreground">
            Long-lived tokens for API and MCP client access
          </p>
        </div>
        <Button
          onClick={() => {
            setShowCreate(!showCreate);
            setCreateError(null);
          }}
          variant={showCreate ? "secondary" : "primary"}
          size="sm"
        >
          {showCreate ? (
            <>
              <XIcon size={14} /> Cancel
            </>
          ) : (
            <>
              <PlusIcon size={14} /> New Token
            </>
          )}
        </Button>
      </div>

      {error && <Alert variant="error">{error}</Alert>}

      {/* New token reveal */}
      {newToken && (
        <TokenRevealCard token={newToken} onDismiss={() => setNewToken(null)} />
      )}

      {/* Create form */}
      {showCreate && (
        <Card className="p-5">
          <h2 className="text-sm font-semibold text-foreground mb-4">Create access token</h2>
          <form onSubmit={handleCreate} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="token-name">Token name</Label>
                <Input
                  id="token-name"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="my-mcp-client"
                  required
                  autoFocus
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="token-expires">Expiry (optional)</Label>
                <Input
                  id="token-expires"
                  type="date"
                  value={expiresAt}
                  onChange={(e) => setExpiresAt(e.target.value)}
                  min={new Date().toISOString().split("T")[0]}
                />
              </div>
            </div>
            <p className="text-xs text-muted-foreground">
              Leave expiry empty to create a non-expiring token. The raw token value is shown
              only once after creation.
            </p>
            {createError && <Alert variant="error">{createError}</Alert>}
            <div className="flex justify-end">
              <Button type="submit" variant="primary" size="sm" disabled={creating || !name.trim()}>
                {creating ? "Creating…" : "Create token"}
              </Button>
            </div>
          </form>
        </Card>
      )}

      {/* Token list */}
      {tokens.length === 0 ? (
        <div className="text-center py-10 text-muted-foreground text-sm">
          No access tokens yet. Create one to use InfraLLM via the API or as an MCP server.
        </div>
      ) : (
        <Card>
          <div className="divide-y divide-border">
            {tokens.map((token) => {
              const expired = isExpired(token.expiresAt);
              const active = token.isActive && !expired;
              return (
                <div
                  key={token.id}
                  className="flex items-center gap-4 px-4 py-3"
                >
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-sm font-medium text-foreground truncate">
                        {token.name}
                      </span>
                      <Badge variant={active ? "success" : expired ? "danger" : "neutral"}>
                        {active ? "Active" : expired ? "Expired" : "Revoked"}
                      </Badge>
                    </div>
                    <div className="text-xs text-muted-foreground mt-0.5 space-x-3">
                      <span>Created {formatDate(token.createdAt)}</span>
                      {token.expiresAt && (
                        <span>
                          {expired ? "Expired" : "Expires"} {formatDate(token.expiresAt)}
                        </span>
                      )}
                      {token.lastUsedAt && (
                        <span>Last used {formatDate(token.lastUsedAt)}</span>
                      )}
                      {!token.lastUsedAt && <span>Never used</span>}
                    </div>
                  </div>
                  {token.isActive && (
                    <Button
                      variant="destructive"
                      size="sm"
                      onClick={() => handleRevoke(token.id)}
                      disabled={revoking === token.id}
                      title="Revoke token"
                    >
                      <TrashIcon size={14} />
                      {revoking === token.id ? "Revoking…" : "Revoke"}
                    </Button>
                  )}
                </div>
              );
            })}
          </div>
        </Card>
      )}

      {/* MCP instructions */}
      <McpInstructions tokens={tokens} />
    </div>
  );
}
