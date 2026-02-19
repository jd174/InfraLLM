"use client";

import { useMemo, useState } from "react";
import { useHosts } from "@/hooks/useHosts";
import { useHostNotes } from "@/hooks/useHostNotes";
import { formatRelativeTime } from "@/lib/utils";
import { Button } from "@/components/ui/Button";
import { Card } from "@/components/ui/Card";
import { RefreshIcon } from "@/components/ui/Icons";

export default function HostNotesPage() {
  const { hosts, loading } = useHosts();
  const hostIds = useMemo(() => hosts.map((host) => host.id), [hosts]);
  const { notes, loading: notesLoading, refreshing, refreshHostNote } = useHostNotes(hostIds);
  const [refreshingAll, setRefreshingAll] = useState(false);

  const handleRefreshAll = async () => {
    if (hosts.length === 0) return;
    setRefreshingAll(true);
    try {
      for (const host of hosts) {
        await refreshHostNote(host.id);
      }
    } finally {
      setRefreshingAll(false);
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
    <div className="p-4 md:p-6 max-w-4xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold text-foreground">Host Notes</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Notes maintained by the LLM for each host.
          </p>
        </div>
        <Button
          variant="secondary"
          size="sm"
          onClick={handleRefreshAll}
          disabled={refreshingAll || refreshing}
        >
          <RefreshIcon size={13} />
          {refreshingAll || refreshing ? "Refreshing..." : "Refresh All"}
        </Button>
      </div>

      {notesLoading && (
        <p className="text-sm text-muted-foreground">Loading notes...</p>
      )}

      {!notesLoading && notes.length === 0 && hosts.length === 0 && (
        <div className="text-center py-16 text-muted-foreground">
          <p className="font-medium">No hosts configured</p>
          <p className="text-sm mt-1">Add hosts to see their notes here</p>
        </div>
      )}

      <div className="space-y-3">
        {hosts.map((host) => {
          const note = notes.find((n) => n.hostId === host.id);
          return (
            <Card key={host.id} className="px-4 py-3 space-y-3">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-sm font-medium text-foreground">{host.name}</p>
                  <p className="text-xs text-muted-foreground mt-0.5">
                    {host.hostname}:{host.port} · {host.type}
                  </p>
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => refreshHostNote(host.id)}
                  disabled={refreshing}
                >
                  <RefreshIcon size={12} />
                  {refreshing ? "Refreshing..." : "Refresh"}
                </Button>
              </div>
              <div className="text-xs text-muted-foreground whitespace-pre-wrap bg-muted/50 rounded-lg p-3 min-h-[48px]">
                {note?.content || "No notes yet."}
              </div>
              {note?.updatedAt && (
                <p className="text-[10px] text-muted-foreground">
                  Updated {formatRelativeTime(note.updatedAt)}
                </p>
              )}
            </Card>
          );
        })}
      </div>
    </div>
  );
}
