"use client";

import { useEffect, useMemo, useState } from "react";
import { useParams, useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { useFeatures } from "@/hooks/useFeatures";
import { useJobs } from "@/hooks/useJobs";
import type { Job, JobRun, UpdateJobRequest } from "@/types";
import { formatDate } from "@/lib/utils";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Textarea } from "@/components/ui/Textarea";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";

const runStatusVariant: Record<string, "success" | "danger" | "warning" | "neutral"> = {
  completed: "success",
  failed: "danger",
  running: "warning",
  pending: "neutral",
};

export default function JobDetailPage() {
  const { chatEnabled } = useFeatures();

  if (!chatEnabled) {
    return (
      <div className="flex h-full items-center justify-center p-6 text-center">
        <div className="space-y-3 max-w-sm">
          <h2 className="text-xl font-semibold text-foreground">Jobs unavailable</h2>
          <p className="text-sm text-muted-foreground">
            Jobs require a configured LLM provider on the server.
          </p>
        </div>
      </div>
    );
  }

  return <JobDetailPageInner />;
}

function JobDetailPageInner() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { updateJob, deleteJob, fetchRuns } = useJobs();
  const [job, setJob] = useState<Job | null>(null);
  const [runs, setRuns] = useState<JobRun[]>([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<UpdateJobRequest>({});

  useEffect(() => {
    Promise.all([
      api.get<Job>(`/api/jobs/${id}`),
      fetchRuns(id),
    ])
      .then(([jobData, runData]) => {
        setJob(jobData);
        setRuns(runData);
      })
      .finally(() => setLoading(false));
  }, [id, fetchRuns]);

  const webhookUrl = useMemo(() => {
    if (!job?.webhookSecret) return null;
    return `/api/jobs/webhook/${job.id}?secret=${job.webhookSecret}`;
  }, [job]);

  const startEditing = () => {
    if (!job) return;
    setForm({
      name: job.name,
      description: job.description || undefined,
      prompt: job.prompt || undefined,
      cronSchedule: job.cronSchedule || undefined,
      autoRunLlm: job.autoRunLlm,
      isEnabled: job.isEnabled,
    });
    setEditing(true);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      const updated = await updateJob(id, form);
      setJob(updated);
      setEditing(false);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async () => {
    await deleteJob(id);
    router.push("/jobs");
  };

  if (loading) return <div className="p-6 text-muted-foreground text-sm">Loading...</div>;
  if (!job) return <div className="p-6 text-destructive text-sm">Job not found</div>;

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      <button
        onClick={() => router.push("/jobs")}
        className="text-sm text-muted-foreground hover:text-foreground mb-4 transition"
      >
        ← Back to Jobs
      </button>

      <Card className="p-5 space-y-5">
        <div className="flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
          <div className="flex items-center gap-3 flex-wrap">
            <h1 className="text-lg font-semibold text-foreground">{job.name}</h1>
            <Badge variant={job.isEnabled ? "success" : "neutral"}>
              {job.isEnabled ? "Enabled" : "Disabled"}
            </Badge>
            <Badge variant="neutral">{job.triggerType}</Badge>
          </div>
          <div className="flex items-center gap-2">
            {!editing && (
              <Button variant="secondary" size="sm" onClick={startEditing}>
                Edit
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

        {editing ? (
          <form onSubmit={handleSave} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="text-xs text-muted-foreground block mb-1">Name</label>
                <Input
                  value={form.name || ""}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  required
                />
              </div>
              {job.triggerType === "Cron" && (
                <div>
                  <label className="text-xs text-muted-foreground block mb-1">Cron Schedule</label>
                  <Input
                    value={form.cronSchedule || ""}
                    onChange={(e) => setForm({ ...form, cronSchedule: e.target.value })}
                    placeholder="*/5 * * * *"
                  />
                </div>
              )}
              {job.triggerType === "Cron" && (
                <div className="sm:col-span-2">
                  <label className="text-xs text-muted-foreground block mb-1">Prompt</label>
                  <Textarea
                    value={form.prompt || ""}
                    onChange={(e) => setForm({ ...form, prompt: e.target.value })}
                    rows={4}
                  />
                </div>
              )}
              <div className="sm:col-span-2">
                <label className="text-xs text-muted-foreground block mb-1">Description</label>
                <Input
                  value={form.description || ""}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                />
              </div>
              {job.triggerType === "Webhook" && (
                <div className="sm:col-span-2">
                  <label className="flex items-center gap-2 text-sm text-foreground cursor-pointer">
                    <input
                      type="checkbox"
                      checked={form.autoRunLlm ?? false}
                      onChange={(e) => setForm({ ...form, autoRunLlm: e.target.checked })}
                      className="rounded border-border accent-primary"
                    />
                    Auto-run LLM investigation
                  </label>
                </div>
              )}
              <div className="sm:col-span-2">
                <label className="flex items-center gap-2 text-sm text-foreground cursor-pointer">
                  <input
                    type="checkbox"
                    checked={form.isEnabled ?? false}
                    onChange={(e) => setForm({ ...form, isEnabled: e.target.checked })}
                    className="rounded border-border accent-primary"
                  />
                  Job enabled
                </label>
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
            <div>
              <p className="text-xs text-muted-foreground">Trigger</p>
              <p className="mt-0.5">{job.triggerType}</p>
            </div>
            <div>
              <p className="text-xs text-muted-foreground">Enabled</p>
              <p className="mt-0.5">{job.isEnabled ? "Yes" : "No"}</p>
            </div>
            {job.triggerType === "Cron" && (
              <div>
                <p className="text-xs text-muted-foreground">Cron</p>
                <p className="font-mono mt-0.5">{job.cronSchedule}</p>
              </div>
            )}
            {job.triggerType === "Cron" && job.prompt && (
              <div className="sm:col-span-2">
                <p className="text-xs text-muted-foreground">Prompt</p>
                <p className="whitespace-pre-wrap text-sm mt-0.5">{job.prompt}</p>
              </div>
            )}
            {job.triggerType === "Webhook" && webhookUrl && (
              <div className="sm:col-span-2">
                <p className="text-xs text-muted-foreground">Webhook URL</p>
                <p className="font-mono text-xs break-all mt-0.5 bg-muted rounded px-2 py-1.5">{webhookUrl}</p>
              </div>
            )}
            {job.description && (
              <div className="sm:col-span-2">
                <p className="text-xs text-muted-foreground">Description</p>
                <p className="mt-0.5">{job.description}</p>
              </div>
            )}
          </div>
        )}
      </Card>

      <div className="mt-6">
        <h2 className="text-sm font-semibold text-foreground mb-3">Recent Runs</h2>
        <div className="space-y-2">
          {runs.map((run) => (
            <Card key={run.id} className="px-4 py-3 space-y-2">
              <div className="flex flex-col sm:flex-row sm:items-center gap-2 justify-between">
                <div>
                  <div className="flex items-center gap-2">
                    <Badge variant={runStatusVariant[run.status.toLowerCase()] ?? "neutral"}>
                      {run.status}
                    </Badge>
                    <span className="text-xs text-muted-foreground">{run.triggeredBy}</span>
                  </div>
                  <p className="text-xs text-muted-foreground mt-0.5">{formatDate(run.createdAt)}</p>
                </div>
                {run.sessionId && (
                  <span className="text-xs text-muted-foreground font-mono">
                    Session {run.sessionId.slice(0, 8)}
                  </span>
                )}
              </div>
              {run.response && (
                <p className="text-xs text-muted-foreground whitespace-pre-wrap border-t border-border pt-2 mt-1">
                  {run.response}
                </p>
              )}
            </Card>
          ))}
          {runs.length === 0 && (
            <div className="text-center py-10 text-muted-foreground text-sm">
              No runs yet
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
