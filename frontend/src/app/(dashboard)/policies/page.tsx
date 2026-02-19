"use client";

import { useEffect, useState } from "react";
import { usePolicies } from "@/hooks/usePolicies";
import { api } from "@/lib/api";
import type { CreatePolicyRequest, PolicyPreset } from "@/types";
import Link from "next/link";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Label } from "@/components/ui/Label";
import { PlusIcon, XIcon, TrashIcon } from "@/components/ui/Icons";
import { cn } from "@/lib/utils";

const presetConfig: Record<string, { border: string; bg: string; text: string }> = {
  "Read-Only Monitoring":  { border: "border-green-500/30",  bg: "bg-green-500/5",  text: "text-green-400" },
  "Service Management":    { border: "border-blue-500/30",   bg: "bg-blue-500/5",   text: "text-blue-400" },
  "Full Access (Dangerous)":{ border: "border-red-500/30",   bg: "bg-red-500/5",    text: "text-red-400" },
  "Approval Required":     { border: "border-yellow-500/30", bg: "bg-yellow-500/5", text: "text-yellow-400" },
};

export default function PoliciesPage() {
  const { policies, loading, error, createPolicy, deletePolicy } = usePolicies();
  const [showCreate, setShowCreate] = useState(false);
  const [presets, setPresets] = useState<PolicyPreset[]>([]);
  const [form, setForm] = useState<CreatePolicyRequest>({
    name: "",
    allowedCommandPatterns: [],
    deniedCommandPatterns: [],
    requireApproval: false,
    maxConcurrentCommands: 5,
  });
  const [allowInput, setAllowInput] = useState("");
  const [denyInput, setDenyInput] = useState("");
  const [creating, setCreating] = useState(false);
  const [activePreset, setActivePreset] = useState<string | null>(null);

  useEffect(() => {
    api.get<PolicyPreset[]>("/api/policies/presets").then(setPresets).catch(() => {});
  }, []);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreating(true);
    try {
      await createPolicy(form);
      setShowCreate(false);
      setActivePreset(null);
      setForm({ name: "", allowedCommandPatterns: [], deniedCommandPatterns: [], requireApproval: false, maxConcurrentCommands: 5 });
    } catch {
      // handled by hook
    } finally {
      setCreating(false);
    }
  };

  const applyPreset = (preset: PolicyPreset) => {
    setForm({
      name: preset.name,
      description: preset.description,
      allowedCommandPatterns: [...preset.allowedCommandPatterns],
      deniedCommandPatterns: [...preset.deniedCommandPatterns],
      requireApproval: preset.requireApproval,
      maxConcurrentCommands: preset.maxConcurrentCommands,
    });
    setActivePreset(preset.name);
    setAllowInput("");
    setDenyInput("");
  };

  const addPattern = (type: "allow" | "deny") => {
    const input = type === "allow" ? allowInput : denyInput;
    const key = type === "allow" ? "allowedCommandPatterns" : "deniedCommandPatterns";
    if (input.trim() && !form[key].includes(input.trim())) {
      setForm({ ...form, [key]: [...form[key], input.trim()] });
      if (type === "allow") {
        setAllowInput("");
      } else {
        setDenyInput("");
      }
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading policies...</p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-lg font-semibold text-foreground">Policies</h1>
          <p className="text-sm text-muted-foreground">
            {policies.length} polic{policies.length !== 1 ? "ies" : "y"} configured
          </p>
        </div>
        <Button
          onClick={() => { setShowCreate(!showCreate); setActivePreset(null); }}
          variant={showCreate ? "secondary" : "primary"}
          size="sm"
        >
          {showCreate ? <><XIcon size={14} /> Cancel</> : <><PlusIcon size={14} /> Create Policy</>}
        </Button>
      </div>

      {error && <Alert className="mb-4">{error}</Alert>}

      {showCreate && (
        <div className="mb-6 space-y-4">
          {/* Preset selector */}
          {presets.length > 0 && (
            <div className="space-y-3">
              <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">Start from a Preset</p>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {presets.map((preset) => {
                  const cfg = presetConfig[preset.name] ?? { border: "border-border", bg: "bg-card", text: "text-foreground" };
                  const isActive = activePreset === preset.name;
                  return (
                    <button
                      key={preset.name}
                      type="button"
                      onClick={() => applyPreset(preset)}
                      className={cn(
                        "text-left p-4 rounded-xl border transition-all",
                        isActive
                          ? `${cfg.border} ${cfg.bg} ring-1 ring-current ${cfg.text}`
                          : "border-border bg-card hover:bg-muted"
                      )}
                    >
                      <p className={cn("text-sm font-medium", isActive && cfg.text)}>{preset.name}</p>
                      <p className="text-xs text-muted-foreground mt-1 leading-relaxed">{preset.description}</p>
                      <div className="flex gap-3 mt-2">
                        <span className="text-xs text-green-400/70">{preset.allowedCommandPatterns.length} allow</span>
                        <span className="text-xs text-red-400/70">{preset.deniedCommandPatterns.length} deny</span>
                        {preset.requireApproval && <span className="text-xs text-yellow-400/70">approval</span>}
                      </div>
                    </button>
                  );
                })}
              </div>
              <div className="flex items-center gap-3 text-xs text-muted-foreground">
                <div className="flex-1 h-px bg-border" />
                <span>or customize below</span>
                <div className="flex-1 h-px bg-border" />
              </div>
            </div>
          )}

          {/* Form */}
          <Card className="p-5">
            <h2 className="text-sm font-semibold text-foreground mb-4">Policy Details</h2>
            <form onSubmit={handleCreate} className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <Label htmlFor="pol-name">Policy Name</Label>
                  <Input
                    id="pol-name"
                    value={form.name}
                    onChange={(e) => setForm({ ...form, name: e.target.value })}
                    required
                    placeholder="Read-Only Access"
                  />
                </div>
                <div>
                  <Label htmlFor="pol-desc">Description</Label>
                  <Input
                    id="pol-desc"
                    value={form.description || ""}
                    onChange={(e) => setForm({ ...form, description: e.target.value || undefined })}
                    placeholder="Optional description"
                  />
                </div>
              </div>

              <div>
                <Label>Allowed Command Patterns (regex)</Label>
                <div className="flex gap-2">
                  <Input
                    value={allowInput}
                    onChange={(e) => setAllowInput(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key !== "Enter") return;
                      e.preventDefault();
                      addPattern("allow");
                    }}
                    className="font-mono text-xs"
                    placeholder="^(cat|ls|df|free|uptime).*"
                  />
                  <Button type="button" variant="secondary" size="sm" onClick={() => addPattern("allow")}>Add</Button>
                </div>
                <div className="flex flex-wrap gap-1.5 mt-2">
                  {form.allowedCommandPatterns.map((p) => (
                    <span key={p} className="inline-flex items-center gap-1 text-xs bg-green-500/10 text-green-400 px-2 py-0.5 rounded font-mono">
                      {p}
                      <button type="button" onClick={() => setForm({ ...form, allowedCommandPatterns: form.allowedCommandPatterns.filter((x) => x !== p) })} className="hover:text-red-400">
                        <XIcon size={10} />
                      </button>
                    </span>
                  ))}
                </div>
              </div>

              <div>
                <Label>Denied Command Patterns (regex)</Label>
                <div className="flex gap-2">
                  <Input
                    value={denyInput}
                    onChange={(e) => setDenyInput(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key !== "Enter") return;
                      e.preventDefault();
                      addPattern("deny");
                    }}
                    className="font-mono text-xs"
                    placeholder="^(rm|mkfs|dd|shutdown).*"
                  />
                  <Button type="button" variant="secondary" size="sm" onClick={() => addPattern("deny")}>Add</Button>
                </div>
                <div className="flex flex-wrap gap-1.5 mt-2">
                  {form.deniedCommandPatterns.map((p) => (
                    <span key={p} className="inline-flex items-center gap-1 text-xs bg-red-500/10 text-red-400 px-2 py-0.5 rounded font-mono">
                      {p}
                      <button type="button" onClick={() => setForm({ ...form, deniedCommandPatterns: form.deniedCommandPatterns.filter((x) => x !== p) })} className="hover:text-red-400">
                        <XIcon size={10} />
                      </button>
                    </span>
                  ))}
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 items-end">
                <div>
                  <Label htmlFor="max-cmd">Max Concurrent Commands</Label>
                  <Input
                    id="max-cmd"
                    type="number"
                    value={form.maxConcurrentCommands}
                    onChange={(e) => setForm({ ...form, maxConcurrentCommands: parseInt(e.target.value) })}
                    min={1}
                  />
                </div>
                <label className="flex items-center gap-2 text-sm text-foreground cursor-pointer pb-2">
                  <input
                    type="checkbox"
                    checked={form.requireApproval}
                    onChange={(e) => setForm({ ...form, requireApproval: e.target.checked })}
                    className="rounded border-border accent-primary"
                  />
                  Require approval for commands
                </label>
              </div>

              <div className="flex justify-end">
                <Button type="submit" disabled={creating}>
                  {creating ? "Creating..." : "Create Policy"}
                </Button>
              </div>
            </form>
          </Card>
        </div>
      )}

      <div className="space-y-2">
        {policies.map((policy) => (
          <Card key={policy.id} className="px-4 py-3">
            <div className="flex flex-col sm:flex-row sm:items-center gap-3">
              <div className="flex-1 min-w-0">
                <Link href={`/policies/${policy.id}`} className="text-sm font-medium text-foreground hover:text-primary transition">
                  {policy.name}
                </Link>
                <p className="text-xs text-muted-foreground mt-0.5">
                  {policy.allowedCommandPatterns.length} allow · {policy.deniedCommandPatterns.length} deny
                  {policy.requireApproval && " · Requires approval"}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant={policy.isEnabled ? "success" : "neutral"}>
                  {policy.isEnabled ? "Active" : "Disabled"}
                </Badge>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => deletePolicy(policy.id)}
                  className="text-muted-foreground hover:text-destructive"
                >
                  <TrashIcon size={13} />
                </Button>
              </div>
            </div>
          </Card>
        ))}
        {policies.length === 0 && !showCreate && (
          <div className="text-center py-16 text-muted-foreground">
            <p className="font-medium">No policies configured yet</p>
            <p className="text-sm mt-1">Create a policy to control command access</p>
          </div>
        )}
      </div>
    </div>
  );
}
