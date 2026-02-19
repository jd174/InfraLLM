"use client";

import { useState } from "react";
import { useHosts } from "@/hooks/useHosts";
import { useCredentials } from "@/hooks/useCredentials";
import { useHostNotes } from "@/hooks/useHostNotes";
import type { CreateHostRequest } from "@/types";
import { HostType } from "@/types";
import Link from "next/link";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Label } from "@/components/ui/Label";
import { PlusIcon, XIcon, TrashIcon, RefreshIcon, NotesIcon } from "@/components/ui/Icons";

const statusVariant: Record<string, "success" | "warning" | "danger" | "neutral"> = {
  Healthy: "success",
  Degraded: "warning",
  Unreachable: "danger",
  Unknown: "neutral",
};

export default function HostsPage() {
  const { hosts, loading, error, createHost, deleteHost } = useHosts();
  const { credentials } = useCredentials();
  const [showCreate, setShowCreate] = useState(false);
  const [form, setForm] = useState<CreateHostRequest>({
    name: "",
    hostname: "",
    port: 22,
    type: HostType.SSH,
    environment: "",
    tags: [],
    allowInsecureSsl: false,
  });
  const [tagInput, setTagInput] = useState("");
  const [creating, setCreating] = useState(false);
  const [selectedHostId, setSelectedHostId] = useState<string | null>(null);
  const [showNotesModal, setShowNotesModal] = useState(false);
  const { notes, loading: notesLoading, refreshing: notesRefreshing, refreshHostNote } = useHostNotes(
    selectedHostId ? [selectedHostId] : []
  );

  const selectedHost = selectedHostId ? hosts.find((h) => h.id === selectedHostId) : null;
  const selectedNote = selectedHostId ? notes.find((n) => n.hostId === selectedHostId) : null;

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreating(true);
    try {
      await createHost(form);
      setShowCreate(false);
      setForm({ name: "", hostname: "", port: 22, type: HostType.SSH, environment: "", tags: [], allowInsecureSsl: false });
    } catch {
      // error handling via hook
    } finally {
      setCreating(false);
    }
  };

  const addTag = () => {
    if (tagInput.trim() && !form.tags.includes(tagInput.trim())) {
      setForm({ ...form, tags: [...form.tags, tagInput.trim()] });
      setTagInput("");
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading hosts...</p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-lg font-semibold text-foreground">Hosts</h1>
          <p className="text-sm text-muted-foreground">
            {hosts.length} host{hosts.length !== 1 ? "s" : ""} configured
          </p>
        </div>
        <Button
          onClick={() => setShowCreate(!showCreate)}
          variant={showCreate ? "secondary" : "primary"}
          size="sm"
        >
          {showCreate ? (
            <><XIcon size={14} /> Cancel</>
          ) : (
            <><PlusIcon size={14} /> Add Host</>
          )}
        </Button>
      </div>

      {error && <Alert className="mb-4">{error}</Alert>}

      {/* Create form */}
      {showCreate && (
        <Card className="mb-6 p-5 space-y-4">
          <h2 className="text-sm font-semibold text-foreground">New Host</h2>
          <form onSubmit={handleCreate} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <Label htmlFor="name">Name</Label>
                <Input
                  id="name"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  required
                  placeholder="my-server"
                />
              </div>
              <div>
                <Label htmlFor="hostname">Hostname / IP</Label>
                <Input
                  id="hostname"
                  value={form.hostname}
                  onChange={(e) => setForm({ ...form, hostname: e.target.value })}
                  required
                  placeholder="192.168.1.10"
                />
              </div>
              <div>
                <Label htmlFor="port">Port</Label>
                <Input
                  id="port"
                  type="number"
                  value={form.port}
                  onChange={(e) => setForm({ ...form, port: parseInt(e.target.value) })}
                  required
                />
              </div>
              <div>
                <Label htmlFor="username">Username</Label>
                <Input
                  id="username"
                  value={form.username || ""}
                  onChange={(e) => setForm({ ...form, username: e.target.value || undefined })}
                  placeholder="root"
                />
              </div>
              <div>
                <Label htmlFor="environment">Environment</Label>
                <Input
                  id="environment"
                  value={form.environment}
                  onChange={(e) => setForm({ ...form, environment: e.target.value })}
                  placeholder="production"
                />
              </div>
              <div>
                <Label htmlFor="credential">Credential</Label>
                <Select
                  id="credential"
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
                {form.tags.length > 0 && (
                  <div className="flex flex-wrap gap-1.5 mt-2">
                    {form.tags.map((tag) => (
                      <span
                        key={tag}
                        className="inline-flex items-center gap-1 text-xs bg-muted text-foreground px-2 py-0.5 rounded"
                      >
                        {tag}
                        <button
                          type="button"
                          onClick={() => setForm({ ...form, tags: form.tags.filter((t) => t !== tag) })}
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
            <div className="flex justify-end">
              <Button type="submit" disabled={creating}>
                {creating ? "Creating..." : "Create Host"}
              </Button>
            </div>
          </form>
        </Card>
      )}

      {/* Host list */}
      <div className="space-y-2">
        {hosts.map((host) => (
          <Card key={host.id} className="px-4 py-3">
            <div className="flex flex-col sm:flex-row sm:items-center gap-3">
              <div className="flex-1 min-w-0">
                <Link
                  href={`/hosts/${host.id}`}
                  className="text-sm font-medium text-foreground hover:text-primary transition"
                >
                  {host.name}
                </Link>
                <p className="text-xs text-muted-foreground mt-0.5 truncate">
                  {host.username ? `${host.username}@` : ""}{host.hostname}:{host.port}
                  {" · "}{host.type}
                  {host.environment ? ` · ${host.environment}` : ""}
                </p>
              </div>
              <div className="flex items-center flex-wrap gap-2">
                {host.tags.map((tag) => (
                  <Badge key={tag} variant="neutral">{tag}</Badge>
                ))}
                <Badge variant={statusVariant[host.status] ?? "neutral"}>
                  {host.status}
                </Badge>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setSelectedHostId(host.id);
                    setShowNotesModal(true);
                  }}
                >
                  <NotesIcon size={13} /> Notes
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => deleteHost(host.id)}
                  className="text-muted-foreground hover:text-destructive"
                >
                  <TrashIcon size={13} />
                </Button>
              </div>
            </div>
          </Card>
        ))}

        {hosts.length === 0 && (
          <div className="text-center py-16 text-muted-foreground">
            <p className="font-medium">No hosts configured yet</p>
            <p className="text-sm mt-1">Add a host to get started</p>
          </div>
        )}
      </div>

      {/* Notes modal */}
      {showNotesModal && selectedHost && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
          <div className="w-full max-w-lg rounded-xl bg-card border border-border p-5 shadow-xl">
            <div className="flex items-start justify-between mb-4">
              <div>
                <h2 className="text-sm font-semibold text-foreground">Host Notes</h2>
                <p className="text-xs text-muted-foreground mt-0.5">
                  {selectedHost.name} · {selectedHost.hostname}:{selectedHost.port}
                </p>
              </div>
              <button
                onClick={() => setShowNotesModal(false)}
                className="p-1 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition"
              >
                <XIcon size={16} />
              </button>
            </div>
            <div className="rounded-lg border border-border bg-input p-3 text-sm text-foreground whitespace-pre-wrap min-h-[120px]">
              {notesLoading ? (
                <span className="text-muted-foreground text-xs">Loading notes...</span>
              ) : (
                selectedNote?.content || <span className="text-muted-foreground text-xs">No notes yet.</span>
              )}
            </div>
            <div className="flex items-center justify-between mt-3">
              <span className="text-xs text-muted-foreground">
                {selectedNote?.updatedAt
                  ? `Updated ${new Date(selectedNote.updatedAt).toLocaleString()}`
                  : ""}
              </span>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => selectedHostId && refreshHostNote(selectedHostId)}
                disabled={notesRefreshing}
              >
                <RefreshIcon size={13} />
                {notesRefreshing ? "Refreshing..." : "Refresh"}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
