"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useFeatures } from "@/hooks/useFeatures";
import { useJobs, type JobRunSummary } from "@/hooks/useJobs";
import { formatRelativeTime } from "@/lib/utils";
import { Card } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";

export default function JobRunsPage() {
  const { chatEnabled } = useFeatures();

  if (!chatEnabled) {
    return (
      <div className="flex h-full items-center justify-center p-6 text-center">
        <div className="space-y-3 max-w-sm">
          <h2 className="text-xl font-semibold text-foreground">Job runs unavailable</h2>
          <p className="text-sm text-muted-foreground">
            Job runs require a configured LLM provider on the server.
          </p>
        </div>
      </div>
    );
  }

  return <JobRunsPageInner />;
}

function JobRunsPageInner() {
  const { fetchRecentRuns } = useJobs();
  const [runs, setRuns] = useState<JobRunSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [expandedRuns, setExpandedRuns] = useState<Set<string>>(new Set());

  useEffect(() => {
    fetchRecentRuns(100)
      .then((data) => setRuns(data))
      .finally(() => setLoading(false));
  }, [fetchRecentRuns]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading job runs...</p>
      </div>
    );
  }

  const toggleExpand = (id: string) => {
    setExpandedRuns((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      <div className="mb-6">
        <h1 className="text-lg font-semibold text-foreground">Job Runs</h1>
        <p className="text-sm text-muted-foreground">Recent automated runs from webhook and cron jobs.</p>
      </div>

      <div className="space-y-2">
        {runs.map((run) => (
          <Card key={run.id} className="px-4 py-3">
            <div className="flex flex-col sm:flex-row sm:items-center gap-2">
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-foreground">{run.jobName}</p>
                <p className="text-xs text-muted-foreground mt-0.5">
                  {run.triggeredBy} · {formatRelativeTime(run.createdAt)}
                </p>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                <Badge
                  variant={
                    run.status === "completed" ? "success"
                    : run.status === "failed" ? "danger"
                    : "neutral"
                  }
                >
                  {run.status}
                </Badge>
                <Link
                  href={`/jobs/${run.jobId}`}
                  className="text-xs text-muted-foreground hover:text-foreground transition"
                >
                  Job
                </Link>
                {run.sessionId && (
                  <Link
                    href={`/chat?sessionId=${run.sessionId}`}
                    className="text-xs text-muted-foreground hover:text-foreground transition"
                  >
                    Chat
                  </Link>
                )}
              </div>
            </div>
            {run.response && (
              <div className="mt-3">
                <div
                  className={`text-sm leading-relaxed bg-muted rounded-lg px-3 py-2 whitespace-pre-wrap break-words ${
                    expandedRuns.has(run.id) ? "" : "line-clamp-3"
                  }`}
                >
                  {run.response}
                </div>
                <button
                  onClick={() => toggleExpand(run.id)}
                  className="mt-1 text-xs text-muted-foreground hover:text-foreground transition"
                >
                  {expandedRuns.has(run.id) ? "Collapse" : "Expand"}
                </button>
              </div>
            )}
          </Card>
        ))}
        {runs.length === 0 && (
          <div className="text-center py-16 text-muted-foreground">
            <p className="font-medium">No job runs yet</p>
            <p className="text-sm mt-1">Runs will appear here once a job is triggered</p>
          </div>
        )}
      </div>
    </div>
  );
}
