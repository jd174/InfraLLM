"use client";

import { useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { api } from "@/lib/api";
import type { Host, UpdateHostRequest, Credential } from "@/types";
import { formatDate } from "@/lib/utils";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Label } from "@/components/ui/Label";
import { XIcon } from "@/components/ui/Icons";

const statusVariant: Record<string, "success" | "warning" | "danger" | "neutral"> = {
  Healthy: "success",
  Degraded: "warning",
  Unreachable: "danger",
  Unknown: "neutral",
};

export default function HostDetailPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [host, setHost] = useState<Host | null>(null);
  const [loading, setLoading] = useState(true);
  const [testing, setTesting] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message?: string } | null>(null);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [credentials, setCredentials] = useState<Credential[]>([]);
  const [form, setForm] = useState<UpdateHostRequest>({});
  const [tagInput, setTagInput] = useState("");

  useEffect(() => {
    Promise.all([
      api.get<Host>(`/api/hosts/${id}`),
      api.get<Credential[]>("/api/credentials"),
    ])
      .then(([hostData, credData]) => {
        setHost(hostData);
        setCredentials(credData);
      })
      .finally(() => setLoading(false));
  }, [id]);

  const startEditing = () => {
    if (!host) return;
    setForm({
      name: host.name,
      hostname: host.hostname,
      port: host.port,
      type: host.type,
      username: host.username || undefined,
      environment: host.environment || undefined,
      tags: [...host.tags],
      credentialId: host.credentialId || undefined,
      allowInsecureSsl: host.allowInsecureSsl,
    });
    setEditing(true);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      const updated = await api.put<Host>(`/api/hosts/${id}`, form);
      setHost(updated);
      setEditing(false);
    } catch {
      // error shown in console
    } finally {
      setSaving(false);
    }
  };

  const addTag = () => {
    if (tagInput.trim() && !(form.tags || []).includes(tagInput.trim())) {
      setForm({ ...form, tags: [...(form.tags || []), tagInput.trim()] });
      setTagInput("");
    }
  };

  const handleTest = async () => {
    setTesting(true);
    setTestResult(null);
    try {
      const result = await api.post<{ success: boolean; message?: string }>(`/api/hosts/${id}/test-connection`);
      setTestResult(result);
    } catch {
      setTestResult({ success: false, message: "Connection test failed" });
    } finally {
      setTesting(false);
    }
  };

  const handleDelete = async () => {
    await api.delete(`/api/hosts/${id}`);
    router.push("/hosts");
  };

  if (loading) return <div className="p-6 text-muted-foreground text-sm">Loading...</div>;
  if (!host) return <div className="p-6 text-destructive text-sm">Host not found</div>;

  return (
    <div className="p-4 md:p-6 max-w-3xl mx-auto">
      <button
        onClick={() => router.push("/hosts")}
        className="text-sm text-muted-foreground hover:text-foreground mb-4 transition"
      >
        ← Back to Hosts
      </button>

      <Card className="p-5 space-y-5">
        <div className="flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
          <div className="flex items-center gap-3 flex-wrap">
            <h1 className="text-lg font-semibold text-foreground">{host.name}</h1>
            <Badge variant={statusVariant[host.status] ?? "neutral"}>{host.status}</Badge>
          </div>
          <div className="flex items-center gap-2 flex-wrap">
            {!editing && (
              <Button variant="secondary" size="sm" onClick={startEditing}>
                Edit
              </Button>
            )}
            <Button variant="secondary" size="sm" onClick={handleTest} disabled={testing}>
              {testing ? "Testing..." : "Test Connection"}
            </Button>
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
          <Alert variant={testResult.success ? "success" : "error"}>
            {testResult.message || (testResult.success ? "Connection successful" : "Connection failed")}
          </Alert>
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
                <Label>Hostname / IP</Label>
                <Input
                  value={form.hostname || ""}
                  onChange={(e) => setForm({ ...form, hostname: e.target.value })}
                  required
                />
              </div>
              <div>
                <Label>Port</Label>
                <Input
                  type="number"
                  value={form.port || 22}
                  onChange={(e) => setForm({ ...form, port: parseInt(e.target.value) })}
                  required
                />
              </div>
              <div>
                <Label>Username</Label>
                <Input
                  value={form.username || ""}
                  onChange={(e) => setForm({ ...form, username: e.target.value || undefined })}
                  placeholder="root"
                />
              </div>
              <div>
                <Label>Environment</Label>
                <Input
                  value={form.environment || ""}
                  onChange={(e) => setForm({ ...form, environment: e.target.value || undefined })}
                  placeholder="production"
                />
              </div>
              <div>
                <Label>Credential</Label>
                <Select
                  value={form.credentialId || ""}
                  onChange={(e) => setForm({ ...form, credentialId: e.target.value || undefined })}
                >
                  <option value="">None</option>
                  {credentials.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name} ({c.credentialType})
                    </option>
                  ))}
                </Select>
              </div>
              <div className="sm:col-span-2">
                <Label>Tags</Label>
                <div className="flex gap-2">
                  <Input
                    value={tagInput}
                    onChange={(e) => setTagInput(e.target.value)}
                    onKeyDown={(e) => e.key === "Enter" && (e.preventDefault(), addTag())}
                    placeholder="Add tag and press Enter"
                  />
                  <Button type="button" variant="secondary" size="sm" onClick={addTag}>
                    Add
                  </Button>
                </div>
                {(form.tags || []).length > 0 && (
                  <div className="flex flex-wrap gap-1.5 mt-2">
                    {(form.tags || []).map((tag) => (
                      <span
                        key={tag}
                        className="inline-flex items-center gap-1 text-xs bg-muted text-foreground px-2 py-0.5 rounded"
                      >
                        {tag}
                        <button
                          type="button"
                          onClick={() => setForm({ ...form, tags: (form.tags || []).filter((t) => t !== tag) })}
                          className="text-muted-foreground hover:text-destructive"
                        >
                          <XIcon size={10} />
                        </button>
                      </span>
                    ))}
                  </div>
                )}
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
          <>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
              <div>
                <p className="text-xs text-muted-foreground">Hostname</p>
                <p className="font-mono mt-0.5">{host.hostname}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Port</p>
                <p className="font-mono mt-0.5">{host.port}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Type</p>
                <p className="mt-0.5">{host.type}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Username</p>
                <p className="mt-0.5">{host.username || "—"}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Environment</p>
                <p className="mt-0.5">{host.environment || "—"}</p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Credential</p>
                <p className="mt-0.5">
                  {host.credentialId
                    ? credentials.find((c) => c.id === host.credentialId)?.name || host.credentialId
                    : "—"}
                </p>
              </div>
              <div>
                <p className="text-xs text-muted-foreground">Created</p>
                <p className="mt-0.5">{formatDate(host.createdAt)}</p>
              </div>
            </div>

            {host.tags.length > 0 && (
              <div>
                <p className="text-xs text-muted-foreground mb-1.5">Tags</p>
                <div className="flex flex-wrap gap-1.5">
                  {host.tags.map((tag) => (
                    <Badge key={tag} variant="neutral">{tag}</Badge>
                  ))}
                </div>
              </div>
            )}
          </>
        )}
      </Card>
    </div>
  );
}
