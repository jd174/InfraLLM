"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import { AuditEventType } from "@/types";
import type { AuditLog, PaginatedResult } from "@/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Badge } from "@/components/ui/Badge";
import { cn } from "@/lib/utils";
import { CheckIcon, XIcon } from "@/components/ui/Icons";

const eventTypeBadgeVariant = (type: string): "success" | "danger" | "default" | "neutral" | "warning" => {
  if (type === "CommandExecuted" || type === "HostAdded" || type === "SessionStarted") return "success";
  if (type === "CommandDenied" || type === "HostRemoved") return "danger";
  if (type === "PolicyChanged") return "default";
  return "neutral";
};

export default function AuditPage() {
  const [logs, setLogs] = useState<AuditLog[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(25);
  const [eventFilter, setEventFilter] = useState<string>("");
  const [commandSearch, setCommandSearch] = useState("");
  const [expandedId, setExpandedId] = useState<string | null>(null);

  const fetchLogs = useCallback(async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() });
      if (eventFilter) params.set("eventType", eventFilter);
      if (commandSearch) params.set("commandContains", commandSearch);
      const result = await api.get<PaginatedResult<AuditLog>>(`/api/audit?${params.toString()}`);
      setLogs(result.items);
      setTotalCount(result.totalCount);
    } catch {
      // error handling
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, eventFilter, commandSearch]);

  useEffect(() => { fetchLogs(); }, [fetchLogs]);

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="p-4 md:p-6 max-w-5xl mx-auto">
      <div className="mb-6">
        <h1 className="text-lg font-semibold text-foreground">Audit Log</h1>
        <p className="text-sm text-muted-foreground">{totalCount} event{totalCount !== 1 ? "s" : ""}</p>
      </div>

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-2 mb-4">
        <Select
          value={eventFilter}
          onChange={(e) => { setEventFilter(e.target.value); setPage(1); }}
          className="sm:w-48"
        >
          <option value="">All Events</option>
          {Object.values(AuditEventType).map((type) => (
            <option key={type} value={type}>{type}</option>
          ))}
        </Select>
        <Input
          value={commandSearch}
          onChange={(e) => { setCommandSearch(e.target.value); setPage(1); }}
          placeholder="Search commands..."
          className="flex-1"
        />
      </div>

      {loading ? (
        <div className="text-center py-16 text-muted-foreground text-sm">Loading...</div>
      ) : (
        <>
          <div className="space-y-1">
            {logs.map((log) => (
              <div key={log.id}>
                <div
                  className="flex items-center gap-3 bg-card border border-border rounded-lg px-3 py-2.5 cursor-pointer hover:bg-muted transition"
                  onClick={() => setExpandedId(expandedId === log.id ? null : log.id)}
                >
                  <Badge variant={eventTypeBadgeVariant(log.eventType)} className="shrink-0 hidden sm:inline-flex">
                    {log.eventType}
                  </Badge>
                  <div className="flex-1 min-w-0">
                    {log.command ? (
                      <code className="text-xs font-mono text-foreground truncate block">{log.command}</code>
                    ) : (
                      <span className="text-xs text-muted-foreground">{log.eventType}</span>
                    )}
                  </div>
                  {log.hostName && (
                    <span className="text-xs text-muted-foreground hidden md:block shrink-0">{log.hostName}</span>
                  )}
                  {log.wasAllowed !== null && (
                    <span className={cn("shrink-0", log.wasAllowed ? "text-green-400" : "text-red-400")}>
                      {log.wasAllowed ? <CheckIcon size={13} /> : <XIcon size={13} />}
                    </span>
                  )}
                  <span className="text-xs text-muted-foreground whitespace-nowrap shrink-0">{formatDate(log.timestamp)}</span>
                </div>

                {expandedId === log.id && (
                  <div className="ml-2 mr-0 mt-1 mb-1 bg-muted border border-border rounded-lg px-4 py-3 text-sm space-y-2">
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                      <div>
                        <p className="text-xs text-muted-foreground">User ID</p>
                        <p className="font-mono text-xs text-foreground">{log.userId}</p>
                      </div>
                      {log.sessionId && (
                        <div>
                          <p className="text-xs text-muted-foreground">Session</p>
                          <p className="font-mono text-xs text-foreground">{log.sessionId}</p>
                        </div>
                      )}
                      {log.denialReason && (
                        <div className="sm:col-span-2">
                          <p className="text-xs text-muted-foreground">Denial Reason</p>
                          <p className="text-xs text-red-400">{log.denialReason}</p>
                        </div>
                      )}
                      {log.llmReasoning && (
                        <div className="sm:col-span-2">
                          <p className="text-xs text-muted-foreground">LLM Reasoning</p>
                          <p className="text-xs text-foreground">{log.llmReasoning}</p>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            ))}
            {logs.length === 0 && (
              <div className="text-center py-16 text-muted-foreground">
                <p className="font-medium">No audit events found</p>
                {(eventFilter || commandSearch) && (
                  <p className="text-sm mt-1">Try adjusting your filters</p>
                )}
              </div>
            )}
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex items-center justify-between mt-4">
              <p className="text-xs text-muted-foreground">Page {page} of {totalPages}</p>
              <div className="flex gap-2">
                <Button variant="secondary" size="sm" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1}>
                  Previous
                </Button>
                <Button variant="secondary" size="sm" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page === totalPages}>
                  Next
                </Button>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
