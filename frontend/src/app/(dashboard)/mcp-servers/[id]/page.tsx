"use client";

import { useEffect, useRef, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { api } from "@/lib/api";
import type {
  McpServer,
  UpdateMcpServerRequest,
  McpTransportType,
  McpTestResult,
  McpToolInfo,
} from "@/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Label } from "@/components/ui/Label";
import { XIcon, PlusIcon } from "@/components/ui/Icons";

interface McpLogEntry {
  timestamp: string;
  level: string;
  message: string;
}

export default function McpServerDetailPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();

  const [server, setServer] = useState<McpServer | null>(null);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<McpTestResult | null>(null);
  const [tools, setTools] = useState<McpToolInfo[] | null>(null);
  const [loadingTools, setLoadingTools] = useState(false);

  const [form, setForm] = useState<UpdateMcpServerRequest>({});
  const [newApiKey, setNewApiKey] = useState("");

  // Log viewer state (stdio servers only)
  const [logsOpen, setLogsOpen] = useState(false);
  const [logs, setLogs] = useState<McpLogEntry[]>([]);
  const logsEndRef = useRef<HTMLDivElement>(null);
  const logsPollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    api.get<McpServer>(`/api/mcp-servers/${id}`)
      .then((data) => setServer(data))
      .finally(() => setLoading(false));
  }, [id]);

  const fetchLogs = async () => {
    try {
      const data = await api.get<McpLogEntry[]>(`/api/mcp-servers/${id}/logs?count=200`);
      setLogs(data);
    } catch {
      // silently ignore — server may not have started yet
    }
  };

  // Start/stop polling when the log panel is opened/closed
  useEffect(() => {
    if (logsOpen) {
      fetchLogs();
      logsPollRef.current = setInterval(fetchLogs, 3000);
    } else {
      if (logsPollRef.current) clearInterval(logsPollRef.current);
    }
    return () => {
      if (logsPollRef.current) clearInterval(logsPollRef.current);
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [logsOpen, id]);

  // Auto-scroll to bottom when new logs arrive
  useEffect(() => {
    if (logsOpen) logsEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [logs, logsOpen]);

  const startEditing = () => {
    if (!server) return;
    setForm({
      name: server.name,
      description: server.description,
      transportType: server.transportType,
      baseUrl: server.baseUrl,
      command: server.command,
      arguments: server.arguments,
      workingDirectory: server.workingDirectory,
      environmentVariables: { ...server.environmentVariables },
      isEnabled: server.isEnabled,
    });
    setNewApiKey("");
    setEditing(true);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      const payload: UpdateMcpServerRequest = { ...form };
      if (newApiKey) payload.apiKey = newApiKey;
      const updated = await api.put<McpServer>(`/api/mcp-servers/${id}`, payload);
      setServer(updated);
      setEditing(false);
      setTestResult(null);
      setTools(null);
    } catch {
      // error handled inline
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    setTesting(true);
    setTestResult(null);
    try {
      const result = await api.post<McpTestResult>(`/api/mcp-servers/${id}/test`);
      setTestResult(result);
    } catch (err) {
      setTestResult({
        success: false,
        toolCount: 0,
        tools: [],
        error: err instanceof Error ? err.message : "Test failed",
      });
    } finally {
      setTesting(false);
    }
  };

  const handleLoadTools = async () => {
    setLoadingTools(true);
    try {
      const data = await api.get<McpToolInfo[]>(`/api/mcp-servers/${id}/tools`);
      setTools(data);
    } catch {
      setTools([]);
    } finally {
      setLoadingTools(false);
    }
  };

  const handleDelete = async () => {
    await api.delete(`/api/mcp-servers/${id}`);
    router.push("/mcp-servers");
  };

  const updateEnvVar = (key: string, value: string) => {
    setForm({ ...form, environmentVariables: { ...(form.environmentVariables || {}), [key]: value } });
  };

  const removeEnvVar = (key: string) => {
    const updated = { ...(form.environmentVariables || {}) };
    delete updated[key];
    setForm({ ...form, environmentVariables: updated });
  };

  const addEnvVar = () => {
    const newKey = `ENV_VAR_${Date.now()}`;
    setForm({ ...form, environmentVariables: { ...(form.environmentVariables || {}), [newKey]: "" } });
  };

  if (loading) return <div className="p-6 text-muted-foreground text-sm">Loading...</div>;
  if (!server) return <div className="p-6 text-destructive text-sm">MCP server not found</div>;

  return (
    <div className="p-4 md:p-6 max-w-3xl mx-auto">
      <button
        onClick={() => router.push("/mcp-servers")}
        className="text-sm text-muted-foreground hover:text-foreground mb-4 transition"
      >
        ← Back to MCP Servers
      </button>

      <Card className="p-5 space-y-5">
        <div className="flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
          <div className="flex items-center gap-3 flex-wrap">
            <h1 className="text-lg font-semibold text-foreground">{server.name}</h1>
            <Badge variant={server.isEnabled ? "success" : "neutral"}>
              {server.isEnabled ? "Enabled" : "Disabled"}
            </Badge>
            <Badge variant="neutral">{server.transportType}</Badge>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            {!editing && (
              <Button variant="secondary" size="sm" onClick={startEditing}>
                Edit
              </Button>
            )}
            <Button variant="secondary" size="sm" onClick={handleTest} disabled={testing}>
              {testing ? "Testing..." : "Test"}
            </Button>
            {!editing && (
              <Button variant="secondary" size="sm" onClick={handleLoadTools} disabled={loadingTools}>
                {loadingTools ? "Loading..." : "List Tools"}
              </Button>
            )}
            <Button
              variant="ghost"
              size="sm"
              onClick={handleDelete}
              className="text-muted-foreground hover:text-destructive"
            >
              Delete
            </Button>
          </div>
        </div>

        {testResult && (
          <Alert variant={testResult.success ? "success" : "error"} className="text-xs">
            {testResult.success ? (
              <>
                <span className="font-medium">
                  {testResult.toolCount} tool{testResult.toolCount !== 1 ? "s" : ""} discovered
                </span>
                {testResult.tools.length > 0 && (
                  <ul className="mt-2 space-y-1">
                    {testResult.tools.map((t) => (
                      <li key={t.name}>
                        <span className="font-mono">{t.name}</span>
                        {t.description && (
                          <span className="opacity-75"> — {t.description}</span>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </>
            ) : (
              testResult.error || "Connection failed"
            )}
          </Alert>
        )}

        {tools !== null && !testResult && (
          <div className="rounded-lg border border-border p-4">
            <h3 className="text-sm font-medium text-foreground mb-2">Available Tools ({tools.length})</h3>
            {tools.length === 0 ? (
              <p className="text-xs text-muted-foreground">No tools discovered</p>
            ) : (
              <ul className="space-y-1">
                {tools.map((t) => (
                  <li key={t.name} className="text-xs">
                    <span className="font-mono text-foreground">{t.name}</span>
                    {t.description && (
                      <span className="text-muted-foreground"> — {t.description}</span>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}

        {editing ? (
          <form onSubmit={handleSave} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <Label>Name</Label>
                <Input
                  value={form.name || ""}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  required
                />
              </div>
              <div>
                <Label>Transport</Label>
                <Select
                  value={form.transportType || "Http"}
                  onChange={(e) => setForm({ ...form, transportType: e.target.value as McpTransportType })}
                >
                  <option value="Http">HTTP</option>
                  <option value="Stdio">Stdio</option>
                </Select>
              </div>
              <div className="sm:col-span-2">
                <Label>Description</Label>
                <Input
                  value={form.description || ""}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                />
              </div>

              {form.transportType === "Http" && (
                <>
                  <div className="sm:col-span-2">
                    <Label>Base URL</Label>
                    <Input
                      value={form.baseUrl || ""}
                      onChange={(e) => setForm({ ...form, baseUrl: e.target.value })}
                      placeholder="http://mcp-server:3000"
                    />
                  </div>
                  <div className="sm:col-span-2">
                    <Label>
                      API Key{" "}
                      {server.hasApiKey && (
                        <span className="font-normal text-green-400">(set — leave blank to keep)</span>
                      )}
                    </Label>
                    <Input
                      type="password"
                      value={newApiKey}
                      onChange={(e) => setNewApiKey(e.target.value)}
                      placeholder={server.hasApiKey ? "Enter new key to replace" : "Optional API key"}
                    />
                  </div>
                </>
              )}

              {form.transportType === "Stdio" && (
                <>
                  <div className="sm:col-span-2">
                    <Label>Command</Label>
                    <Input
                      value={form.command || ""}
                      onChange={(e) => setForm({ ...form, command: e.target.value })}
                      placeholder="npx"
                    />
                  </div>
                  <div className="sm:col-span-2">
                    <Label>Arguments</Label>
                    <Input
                      value={form.arguments || ""}
                      onChange={(e) => setForm({ ...form, arguments: e.target.value })}
                      placeholder="-y @modelcontextprotocol/server-filesystem /data"
                    />
                  </div>
                </>
              )}

              <div className="sm:col-span-2">
                <label className="flex items-center gap-2 text-sm text-foreground cursor-pointer">
                  <input
                    type="checkbox"
                    checked={form.isEnabled ?? true}
                    onChange={(e) => setForm({ ...form, isEnabled: e.target.checked })}
                    className="rounded border-border accent-primary"
                  />
                  Enabled
                </label>
              </div>

              <div className="sm:col-span-2 space-y-2">
                <div className="flex items-center justify-between">
                  <Label>Environment Variables</Label>
                  <Button type="button" variant="ghost" size="sm" onClick={addEnvVar}>
                    <PlusIcon size={12} /> Add
                  </Button>
                </div>
                {Object.entries(form.environmentVariables || {}).map(([key, value]) => (
                  <div key={key} className="flex gap-2">
                    <Input
                      value={key}
                      onChange={(e) => {
                        const updated = { ...(form.environmentVariables || {}) };
                        const val = updated[key];
                        delete updated[key];
                        updated[e.target.value] = val;
                        setForm({ ...form, environmentVariables: updated });
                      }}
                      className="font-mono text-xs"
                      placeholder="KEY"
                    />
                    <Input
                      value={value}
                      onChange={(e) => updateEnvVar(key, e.target.value)}
                      className="text-xs"
                      placeholder="value"
                    />
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={() => removeEnvVar(key)}
                      className="text-muted-foreground hover:text-destructive shrink-0"
                    >
                      <XIcon size={13} />
                    </Button>
                  </div>
                ))}
              </div>
            </div>

            <div className="flex justify-end gap-2">
              <Button type="button" variant="secondary" onClick={() => setEditing(false)}>
                Cancel
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? "Saving..." : "Save Changes"}
              </Button>
            </div>
          </form>
        ) : (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
            {server.baseUrl && (
              <div className="sm:col-span-2">
                <p className="text-xs text-muted-foreground">Base URL</p>
                <p className="font-mono mt-0.5">{server.baseUrl}</p>
              </div>
            )}
            {server.hasApiKey && (
              <div>
                <p className="text-xs text-muted-foreground">API Key</p>
                <p className="text-green-400 text-xs mt-0.5">Configured</p>
              </div>
            )}
            {server.description && (
              <div className="sm:col-span-2">
                <p className="text-xs text-muted-foreground">Description</p>
                <p className="mt-0.5">{server.description}</p>
              </div>
            )}
            {server.command && (
              <div className="sm:col-span-2">
                <p className="text-xs text-muted-foreground">Command</p>
                <p className="font-mono mt-0.5">{server.command} {server.arguments}</p>
              </div>
            )}
            {Object.keys(server.environmentVariables).length > 0 && (
              <div className="sm:col-span-2">
                <p className="text-xs text-muted-foreground">Environment Variables</p>
                <div className="mt-1 space-y-0.5">
                  {Object.entries(server.environmentVariables).map(([k]) => (
                    <p key={k} className="font-mono text-xs">
                      {k} = <span className="text-muted-foreground">***</span>
                    </p>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}
      </Card>

      {/* Log viewer — stdio servers only */}
      {server.transportType === "Stdio" && (
        <div className="mt-4">
          <button
            onClick={() => setLogsOpen((o) => !o)}
            className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground transition w-full text-left"
          >
            <span className={`transition-transform ${logsOpen ? "rotate-90" : ""}`}>▶</span>
            Process Logs
            {logs.length > 0 && (
              <span className="ml-auto text-xs text-muted-foreground">{logs.length} entries</span>
            )}
          </button>

          {logsOpen && (
            <div className="mt-2 rounded-lg border border-border bg-black/80 p-3 font-mono text-xs overflow-y-auto max-h-80 space-y-0.5">
              {logs.length === 0 ? (
                <p className="text-muted-foreground italic">No logs yet — process may still be starting…</p>
              ) : (
                logs.map((entry, i) => (
                  <div key={i} className="flex gap-2 leading-relaxed">
                    <span className="text-muted-foreground shrink-0 tabular-nums">
                      {new Date(entry.timestamp).toLocaleTimeString()}
                    </span>
                    <span className={
                      entry.level === "error" ? "text-red-400" :
                      entry.level === "warn" ? "text-yellow-400" :
                      entry.level === "stderr" ? "text-orange-300" :
                      "text-green-300"
                    }>
                      [{entry.level}]
                    </span>
                    <span className="text-gray-200 break-all">{entry.message}</span>
                  </div>
                ))
              )}
              <div ref={logsEndRef} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
