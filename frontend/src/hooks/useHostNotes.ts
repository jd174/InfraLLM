"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { HostNote } from "@/types";

export function useHostNotes(hostIds: string[]) {
  const [notes, setNotes] = useState<HostNote[]>([]);
  const [loading, setLoading] = useState(false);
  const [refreshing, setRefreshing] = useState(false);

  const hostIdsParam = useMemo(() => hostIds.filter(Boolean).join(","), [hostIds]);

  const fetchNotes = useCallback(async () => {
    if (!hostIdsParam) {
      setNotes([]);
      return;
    }

    try {
      setLoading(true);
      const data = await api.get<HostNote[]>(`/api/hosts/notes?hostIds=${hostIdsParam}`);
      setNotes(data);
    } finally {
      setLoading(false);
    }
  }, [hostIdsParam]);

  const refreshHostNote = useCallback(
    async (hostId: string) => {
      if (!hostId) return null;
      try {
        setRefreshing(true);
        const updated = await api.post<HostNote>(`/api/hosts/${hostId}/notes/refresh`, {});
        setNotes((prev) => {
          const existing = prev.find((n) => n.hostId === hostId);
          if (!existing) return [...prev, updated];
          return prev.map((n) => (n.hostId === hostId ? updated : n));
        });
        return updated;
      } finally {
        setRefreshing(false);
      }
    },
    []
  );

  useEffect(() => {
    fetchNotes();
  }, [fetchNotes]);

  return { notes, loading, refreshing, refresh: fetchNotes, refreshHostNote };
}
