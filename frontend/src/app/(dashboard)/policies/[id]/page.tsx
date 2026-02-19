"use client";

import { useEffect, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { api } from "@/lib/api";
import type { Policy, PolicyTestResult, PolicyAssignment, Host } from "@/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { CheckIcon, XIcon } from "@/components/ui/Icons";

export default function PolicyDetailPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [policy, setPolicy] = useState<Policy | null>(null);
  const [loading, setLoading] = useState(true);
  const [testCommand, setTestCommand] = useState("");
  const [testResult, setTestResult] = useState<PolicyTestResult | null>(null);
  const [testing, setTesting] = useState(false);

  const [assignments, setAssignments] = useState<PolicyAssignment[]>([]);
  const [hosts, setHosts] = useState<Host[]>([]);
  const [assignHostId, setAssignHostId] = useState<string>("");
  const [assigning, setAssigning] = useState(false);

  useEffect(() => {
    Promise.all([
      api.get<Policy>(`/api/policies/${id}`),
      api.get<PolicyAssignment[]>(`/api/policies/${id}/assignments`),
      api.get<Host[]>("/api/hosts"),
    ])
      .then(([policyData, assignmentData, hostData]) => {
        setPolicy(policyData);
        setAssignments(assignmentData);
        setHosts(hostData);
      })
      .finally(() => setLoading(false));
  }, [id]);

  const handleTest = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!testCommand.trim()) return;
    setTesting(true);
    setTestResult(null);
    try {
      const result = await api.post<PolicyTestResult>(`/api/policies/${id}/test`, { command: testCommand });
      setTestResult(result);
    } catch {
      setTestResult({ isAllowed: false, denialReason: "Request failed", requiresApproval: false, matchedPattern: null });
    } finally {
      setTesting(false);
    }
  };

  const handleAssign = async () => {
    setAssigning(true);
    try {
      const body: { userId?: string; hostId?: string } = {};
      if (assignHostId) body.hostId = assignHostId;
      await api.post(`/api/policies/${id}/assignments`, body);
      const updated = await api.get<PolicyAssignment[]>(`/api/policies/${id}/assignments`);
      setAssignments(updated);
      setAssignHostId("");
    } catch {
      // handled
    } finally {
      setAssigning(false);
    }
  };

  const handleRemoveAssignment = async (assignmentId: string) => {
    try {
      await api.delete(`/api/policies/${id}/assignments/${assignmentId}`);
      setAssignments((prev) => prev.filter((a) => a.id !== assignmentId));
    } catch {
      // handled
    }
  };

  const handleDelete = async () => {
    await api.delete(`/api/policies/${id}`);
    router.push("/policies");
  };

  if (loading) return <div className="p-6 text-muted-foreground text-sm">Loading...</div>;
  if (!policy) return <div className="p-6 text-destructive text-sm">Policy not found</div>;

  return (
    <div className="p-4 md:p-6 max-w-3xl mx-auto space-y-4">
      <button
        onClick={() => router.push("/policies")}
        className="text-sm text-muted-foreground hover:text-foreground transition"
      >
        ← Back to Policies
      </button>

      {/* Policy Details */}
      <Card className="p-5 space-y-4">
        <div className="flex items-center justify-between">
          <h1 className="text-lg font-semibold text-foreground">{policy.name}</h1>
          <Button
            variant="ghost"
            size="sm"
            onClick={handleDelete}
            className="text-muted-foreground hover:text-destructive"
          >
            Delete
          </Button>
        </div>

        {policy.description && (
          <p className="text-sm text-muted-foreground">{policy.description}</p>
        )}

        <div className="space-y-2">
          <p className="text-xs font-medium text-green-400 uppercase tracking-wide">Allowed Patterns</p>
          {policy.allowedCommandPatterns.length > 0 ? (
            <div className="flex flex-wrap gap-1.5">
              {policy.allowedCommandPatterns.map((p) => (
                <code key={p} className="text-xs bg-green-500/10 text-green-400 px-2 py-0.5 rounded font-mono">
                  {p}
                </code>
              ))}
            </div>
          ) : (
            <p className="text-xs text-muted-foreground">None — all commands denied by default</p>
          )}
        </div>

        <div className="space-y-2">
          <p className="text-xs font-medium text-red-400 uppercase tracking-wide">Denied Patterns</p>
          {policy.deniedCommandPatterns.length > 0 ? (
            <div className="flex flex-wrap gap-1.5">
              {policy.deniedCommandPatterns.map((p) => (
                <code key={p} className="text-xs bg-red-500/10 text-red-400 px-2 py-0.5 rounded font-mono">
                  {p}
                </code>
              ))}
            </div>
          ) : (
            <p className="text-xs text-muted-foreground">None</p>
          )}
        </div>

        <div className="grid grid-cols-2 gap-4 text-sm pt-2 border-t border-border">
          <div>
            <p className="text-xs text-muted-foreground">Max Concurrent</p>
            <p className="mt-0.5">{policy.maxConcurrentCommands}</p>
          </div>
          <div>
            <p className="text-xs text-muted-foreground">Require Approval</p>
            <p className="mt-0.5">{policy.requireApproval ? "Yes" : "No"}</p>
          </div>
        </div>
      </Card>

      {/* Assignments */}
      <Card className="p-5 space-y-4">
        <div>
          <h2 className="text-sm font-semibold text-foreground">Assignments</h2>
          <p className="text-xs text-muted-foreground mt-1">
            Assign this policy to yourself for specific hosts or all hosts. Only assigned policies are enforced.
          </p>
        </div>

        <div className="flex flex-col sm:flex-row gap-2 items-end">
          <div className="flex-1">
            <label className="text-xs text-muted-foreground block mb-1">Scope</label>
            <Select
              value={assignHostId}
              onChange={(e) => setAssignHostId(e.target.value)}
            >
              <option value="">All Hosts (global)</option>
              {hosts.map((h) => (
                <option key={h.id} value={h.id}>
                  {h.name} ({h.hostname})
                </option>
              ))}
            </Select>
          </div>
          <Button onClick={handleAssign} disabled={assigning} className="whitespace-nowrap">
            {assigning ? "Assigning..." : "Assign to Me"}
          </Button>
        </div>

        {assignments.length > 0 ? (
          <div className="space-y-2">
            {assignments.map((a) => (
              <div
                key={a.id}
                className="flex items-center justify-between text-sm rounded-lg px-3 py-2.5 border border-border"
              >
                <div className="flex items-center gap-2 text-xs">
                  <span className="text-muted-foreground font-mono">{a.userId.slice(0, 8)}…</span>
                  <span className="text-muted-foreground">→</span>
                  {a.hostId ? (
                    <span className="text-blue-400">{a.hostName || a.hostId.slice(0, 8)}</span>
                  ) : (
                    <span className="text-yellow-400">All Hosts</span>
                  )}
                </div>
                <button
                  onClick={() => handleRemoveAssignment(a.id)}
                  className="text-xs text-muted-foreground hover:text-destructive transition"
                >
                  Remove
                </button>
              </div>
            ))}
          </div>
        ) : (
          <div className="text-center py-6 text-muted-foreground text-xs border border-dashed border-border rounded-lg">
            <p>No assignments yet</p>
            <p className="mt-0.5">This policy won&apos;t take effect until assigned</p>
          </div>
        )}
      </Card>

      {/* Test Command */}
      <Card className="p-5 space-y-4">
        <div>
          <h2 className="text-sm font-semibold text-foreground">Test Command</h2>
          <p className="text-xs text-muted-foreground mt-1">
            Test whether a command would be allowed or denied by this policy&apos;s patterns.
          </p>
        </div>
        <form onSubmit={handleTest} className="flex gap-2">
          <Input
            value={testCommand}
            onChange={(e) => setTestCommand(e.target.value)}
            placeholder="ls -la /var/log"
            className="font-mono text-sm flex-1"
          />
          <Button type="submit" disabled={testing}>
            {testing ? "Testing..." : "Test"}
          </Button>
        </form>
        {testResult && (
          <Alert variant={testResult.isAllowed ? "success" : "error"}>
            <div className="flex items-center gap-1.5 font-medium">
              {testResult.isAllowed ? <CheckIcon size={13} /> : <XIcon size={13} />}
              {testResult.isAllowed ? "Allowed" : "Denied"}
              {testResult.requiresApproval && " (requires approval)"}
            </div>
            {testResult.denialReason && (
              <p className="text-xs mt-1 opacity-80">{testResult.denialReason}</p>
            )}
            {testResult.matchedPattern && (
              <p className="text-xs mt-1 font-mono opacity-80">Matched: {testResult.matchedPattern}</p>
            )}
          </Alert>
        )}
      </Card>
    </div>
  );
}
