"use client";

import { useState } from "react";
import Link from "next/link";
import { useMcpServers } from "@/hooks/useMcpServers";
import type { CreateMcpServerRequest, McpTransportType, McpTestResult } from "@/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Label } from "@/components/ui/Label";
import { PlusIcon, XIcon, TrashIcon, EditIcon, ServerIcon } from "@/components/ui/Icons";

const defaultForm: CreateMcpServerRequest = {
  name: "",
  description: "",
  transportType: "Http",
  baseUrl: "",
  environmentVariables: {},
  isEnabled: true,
};

export default function McpServersPage() {
  const { servers, loading, error, createServer, deleteServer, testServer } = useMcpServers();
  const [showCreate, setShowCreate] = useState(false);
  const [form, setForm] = useState<CreateMcpServerRequest>(defaultForm);
  const [creating, setCreating] = useState(false);
  const [testingId, setTestingId] = useState<string | null>(null);
  const [testResults, setTestResults] = useState<Record<string, McpTestResult>>({});

  const [envInput, setEnvInput] = useState<{ key: string; value: string }[]>([]);

  const addEnvRow = () => setEnvInput((prev) => [...prev, { key: "", value: "" }]);
  const removeEnvRow = (i: number) => setEnvInput((prev) => prev.filter((_, idx) => idx !== i));
  const updateEnvRow = (i: number, field: "key" | "value", val: string) =>
    setEnvInput((prev) => prev.map((row, idx) => (idx === i ? { ...row, [field]: val } : row)));

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreating(true);
    try {
      const envVars = Object.fromEntries(
        envInput.filter((r) => r.key.trim()).map((r) => [r.key.trim(), r.value])
      );
      await createServer({
        ...form,
        description: form.description || undefined,
        baseUrl: form.baseUrl || undefined,
        apiKey: form.apiKey || undefined,
        command: form.command || undefined,
        arguments: form.arguments || undefined,
        workingDirectory: form.workingDirectory || undefined,
        environmentVariables: Object.keys(envVars).length > 0 ? envVars : undefined,
      });
      setShowCreate(false);
      setForm(defaultForm);
      setEnvInput([]);
    } catch {
      // error shown via hook
    } finally {
      setCreating(false);
    }
  };

  const handleTest = async (id: string) => {
    setTestingId(id);
    try {
      const result = await testServer(id);
      setTestResults((prev) => ({ ...prev, [id]: result }));
    } catch (err) {
      setTestResults((prev) => ({
        ...prev,
        [id]: {
          success: false,
          toolCount: 0,
          tools: [],
          error: err instanceof Error ? err.message : "Test failed",
        },
      }));
    } finally {
      setTestingId(null);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading MCP servers...</p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-lg font-semibold text-foreground">MCP Servers</h1>
          <p className="text-sm text-muted-foreground">
            {servers.length} server{servers.length !== 1 ? "s" : ""} configured
          </p>
        </div>
        <Button
          onClick={() => setShowCreate(!showCreate)}
          variant={showCreate ? "secondary" : "primary"}
          size="sm"
        >
          {showCreate ? <><XIcon size={14} /> Cancel</> : <><PlusIcon size={14} /> Add Server</>}
        </Button>
      </div>

      {error && <Alert className="mb-4">{error}</Alert>}

      {showCreate && (
        <Card className="mb-6 p-5">
          <h2 className="text-sm font-semibold text-foreground mb-4">New MCP Server</h2>
          <form onSubmit={handleCreate} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <Label htmlFor="mcp-name">Name</Label>
                <Input
                  id="mcp-name"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  required
                  placeholder="my-mcp-server"
                />
              </div>
              <div>
                <Label htmlFor="mcp-transport">Transport</Label>
                <Select
                  id="mcp-transport"
                  value={form.transportType}
                  onChange={(e) => {
                    setForm({ ...form, transportType: e.target.value as McpTransportType });
                    setEnvInput([]);
                  }}
                >
                  <option value="Http">HTTP</option>
                  <option value="Stdio">Stdio</option>
                </Select>
              </div>
              <div className="sm:col-span-2">
                <Label htmlFor="mcp-desc">Description</Label>
                <Input
                  id="mcp-desc"
                  value={form.description || ""}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                  placeholder="Optional description"
                />
              </div>

              {form.transportType === "Http" && (
                <>
                  <div className="sm:col-span-2">
                    <Label htmlFor="mcp-url">Base URL</Label>
                    <Input
                      id="mcp-url"
                      value={form.baseUrl || ""}
                      onChange={(e) => setForm({ ...form, baseUrl: e.target.value })}
                      required
                      placeholder="http://mcp-server:3000"
                    />
                  </div>
                  <div className="sm:col-span-2">
                    <Label htmlFor="mcp-key">API Key <span className="font-normal text-muted-foreground">(optional)</span></Label>
                    <Input
                      id="mcp-key"
                      type="password"
                      value={form.apiKey || ""}
                      onChange={(e) => setForm({ ...form, apiKey: e.target.value })}
                      placeholder="Stored encrypted"
                    />
                  </div>
                </>
              )}

              {form.transportType === "Stdio" && (
                <>
                  <div className="sm:col-span-2">
                    <Label htmlFor="mcp-command">Command</Label>
                    <Input
                      id="mcp-command"
                      value={form.command || ""}
                      onChange={(e) => setForm({ ...form, command: e.target.value })}
                      required
                      placeholder="npx"
                      className="font-mono"
                    />
                  </div>
                  <div className="sm:col-span-2">
                    <Label htmlFor="mcp-args">Arguments</Label>
                    <Input
                      id="mcp-args"
                      value={form.arguments || ""}
                      onChange={(e) => setForm({ ...form, arguments: e.target.value })}
                      placeholder="-y @modelcontextprotocol/server-filesystem /data"
                      className="font-mono"
                    />
                  </div>
                  <div className="sm:col-span-2">
                    <Label htmlFor="mcp-cwd">Working Directory <span className="font-normal text-muted-foreground">(optional)</span></Label>
                    <Input
                      id="mcp-cwd"
                      value={form.workingDirectory || ""}
                      onChange={(e) => setForm({ ...form, workingDirectory: e.target.value })}
                      placeholder="/app"
                      className="font-mono"
                    />
                  </div>
                  <div className="sm:col-span-2 space-y-2">
                    <div className="flex items-center justify-between">
                      <Label>Environment Variables <span className="font-normal text-muted-foreground">(optional)</span></Label>
                      <Button type="button" variant="ghost" size="sm" onClick={addEnvRow}>
                        <PlusIcon size={12} /> Add
                      </Button>
                    </div>
                    {envInput.map((row, i) => (
                      <div key={i} className="flex gap-2">
                        <Input
                          value={row.key}
                          onChange={(e) => updateEnvRow(i, "key", e.target.value)}
                          placeholder="KEY"
                          className="font-mono text-xs"
                        />
                        <Input
                          value={row.value}
                          onChange={(e) => updateEnvRow(i, "value", e.target.value)}
                          placeholder="value"
                          className="text-xs"
                        />
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          onClick={() => removeEnvRow(i)}
                          className="text-muted-foreground hover:text-destructive shrink-0"
                        >
                          <XIcon size={13} />
                        </Button>
                      </div>
                    ))}
                  </div>
                </>
              )}
            </div>
            <div className="flex justify-end">
              <Button type="submit" disabled={creating}>
                {creating ? "Creating..." : "Create Server"}
              </Button>
            </div>
          </form>
        </Card>
      )}

      <div className="space-y-2">
        {servers.map((server) => {
          const testResult = testResults[server.id];
          return (
            <Card key={server.id} className="px-4 py-3 space-y-2">
              <div className="flex flex-col sm:flex-row sm:items-center gap-3">
                <div className="flex items-start gap-3 flex-1 min-w-0">
                  <ServerIcon size={14} className="text-muted-foreground mt-0.5 shrink-0" />
                  <div className="min-w-0">
                    <Link href={`/mcp-servers/${server.id}`} className="text-sm font-medium text-foreground hover:text-primary transition">
                      {server.name}
                    </Link>
                    <p className="text-xs text-muted-foreground mt-0.5 truncate">
                      {server.transportType === "Http" ? server.baseUrl || "No URL" : server.command || "No command"}
                      {" · "}{server.transportType}
                    </p>
                    {server.description && (
                      <p className="text-xs text-muted-foreground mt-0.5">{server.description}</p>
                    )}
                  </div>
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  <Badge variant={server.isEnabled ? "success" : "neutral"}>
                    {server.isEnabled ? "Enabled" : "Disabled"}
                  </Badge>
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => handleTest(server.id)}
                    disabled={testingId === server.id}
                  >
                    {testingId === server.id ? "Testing..." : "Test"}
                  </Button>
                  <Link href={`/mcp-servers/${server.id}`}>
                    <Button variant="ghost" size="sm">
                      <EditIcon size={13} />
                    </Button>
                  </Link>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => deleteServer(server.id)}
                    className="text-muted-foreground hover:text-destructive"
                  >
                    <TrashIcon size={13} />
                  </Button>
                </div>
              </div>

              {testResult && (
                <Alert variant={testResult.success ? "success" : "error"} className="text-xs">
                  {testResult.success
                    ? <><span className="font-medium">{testResult.toolCount} tool{testResult.toolCount !== 1 ? "s" : ""} available:</span> {testResult.tools.map((t) => t.name).join(", ") || "none"}</>
                    : testResult.error || "Connection failed"
                  }
                </Alert>
              )}
            </Card>
          );
        })}
        {servers.length === 0 && (
          <div className="text-center py-16 text-muted-foreground">
            <p className="font-medium">No MCP servers configured yet</p>
            <p className="text-sm mt-1">Add an MCP server to give the AI access to external tools</p>
          </div>
        )}
      </div>
    </div>
  );
}
