"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useJobs } from "@/hooks/useJobs";
import type { CreateJobRequest, JobTriggerType } from "@/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Label } from "@/components/ui/Label";
import { PlusIcon, XIcon, TrashIcon } from "@/components/ui/Icons";

export default function JobsPage() {
  const { jobs, loading, error, createJob, deleteJob } = useJobs();
  const [showCreate, setShowCreate] = useState(false);
  const [creating, setCreating] = useState(false);
  const [form, setForm] = useState<CreateJobRequest>({
    name: "",
    description: "",
    prompt: "",
    triggerType: "Webhook",
    cronSchedule: "",
    autoRunLlm: true,
    isEnabled: true,
  });

  const webhookUrlPreview = useMemo(() => "/api/jobs/webhook/{jobId}?secret={secret}", []);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreating(true);
    try {
      const payload: CreateJobRequest = {
        ...form,
        cronSchedule: form.triggerType === "Cron" ? form.cronSchedule : undefined,
        prompt: form.triggerType === "Cron" ? form.prompt : undefined,
        autoRunLlm: form.triggerType === "Webhook" ? form.autoRunLlm : false,
      };
      await createJob(payload);
      setShowCreate(false);
      setForm({ name: "", description: "", prompt: "", triggerType: "Webhook", cronSchedule: "", autoRunLlm: true, isEnabled: true });
    } finally {
      setCreating(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading jobs...</p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-lg font-semibold text-foreground">Jobs</h1>
          <p className="text-sm text-muted-foreground">
            {jobs.length} job{jobs.length !== 1 ? "s" : ""} configured
          </p>
        </div>
        <Button
          onClick={() => setShowCreate(!showCreate)}
          variant={showCreate ? "secondary" : "primary"}
          size="sm"
        >
          {showCreate ? <><XIcon size={14} /> Cancel</> : <><PlusIcon size={14} /> New Job</>}
        </Button>
      </div>

      {error && <Alert className="mb-4">{error}</Alert>}

      {/* Create form */}
      {showCreate && (
        <Card className="mb-6 p-5">
          <h2 className="text-sm font-semibold text-foreground mb-4">Create Job</h2>
          <form onSubmit={handleCreate} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <Label htmlFor="name">Name</Label>
                <Input
                  id="name"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  required
                  placeholder="Daily Health Check"
                />
              </div>
              <div>
                <Label htmlFor="trigger">Trigger</Label>
                <Select
                  id="trigger"
                  value={form.triggerType}
                  onChange={(e) => setForm({ ...form, triggerType: e.target.value as JobTriggerType })}
                >
                  <option value="Webhook">Webhook</option>
                  <option value="Cron">Cron</option>
                </Select>
              </div>
              <div className="sm:col-span-2">
                <Label htmlFor="description">Description</Label>
                <Input
                  id="description"
                  value={form.description || ""}
                  onChange={(e) => setForm({ ...form, description: e.target.value })}
                  placeholder="Analyze alerts and propose fixes"
                />
              </div>

              {form.triggerType === "Cron" && (
                <>
                  <div className="sm:col-span-2">
                    <Label htmlFor="cron">Cron Schedule</Label>
                    <Input
                      id="cron"
                      value={form.cronSchedule || ""}
                      onChange={(e) => setForm({ ...form, cronSchedule: e.target.value })}
                      required
                      placeholder="*/5 * * * *"
                    />
                  </div>
                  <div className="sm:col-span-2">
                    <Label htmlFor="prompt">Prompt</Label>
                    <Textarea
                      id="prompt"
                      value={form.prompt || ""}
                      onChange={(e) => setForm({ ...form, prompt: e.target.value })}
                      required
                      rows={4}
                      placeholder="Describe what the LLM should check on this schedule..."
                    />
                  </div>
                </>
              )}

              {form.triggerType === "Webhook" && (
                <div className="sm:col-span-2 space-y-3">
                  <p className="text-xs text-muted-foreground font-mono bg-muted rounded px-3 py-2 break-all">
                    {webhookUrlPreview}
                  </p>
                  <label className="flex items-center gap-2 text-sm text-foreground cursor-pointer">
                    <input
                      type="checkbox"
                      checked={form.autoRunLlm || false}
                      onChange={(e) => setForm({ ...form, autoRunLlm: e.target.checked })}
                      className="rounded border-border accent-primary"
                    />
                    Auto-run LLM investigation when webhook arrives
                  </label>
                </div>
              )}
            </div>

            <div className="flex justify-end">
              <Button type="submit" disabled={creating}>
                {creating ? "Creating..." : "Create Job"}
              </Button>
            </div>
          </form>
        </Card>
      )}

      {/* Job list */}
      <div className="space-y-2">
        {jobs.map((job) => (
          <Card key={job.id} className="px-4 py-3">
            <div className="flex flex-col sm:flex-row sm:items-center gap-3">
              <div className="flex-1 min-w-0">
                <Link
                  href={`/jobs/${job.id}`}
                  className="text-sm font-medium text-foreground hover:text-primary transition"
                >
                  {job.name}
                </Link>
                <p className="text-xs text-muted-foreground mt-0.5">
                  {job.triggerType}{job.triggerType === "Cron" ? ` · ${job.cronSchedule}` : ""}
                  {job.description ? ` · ${job.description}` : ""}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant={job.isEnabled ? "success" : "neutral"}>
                  {job.isEnabled ? "Enabled" : "Disabled"}
                </Badge>
                <Badge variant="neutral">{job.triggerType}</Badge>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => deleteJob(job.id)}
                  className="text-muted-foreground hover:text-destructive"
                >
                  <TrashIcon size={13} />
                </Button>
              </div>
            </div>
          </Card>
        ))}

        {jobs.length === 0 && (
          <div className="text-center py-16 text-muted-foreground">
            <p className="font-medium">No jobs configured yet</p>
            <p className="text-sm mt-1">Create a job to accept webhooks or run on a schedule</p>
          </div>
        )}
      </div>
    </div>
  );
}
