"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Host, CreateHostRequest, UpdateHostRequest } from "@/types";

export function useHosts() {
  const [hosts, setHosts] = useState<Host[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchHosts = useCallback(async () => {
    try {
      setLoading(true);
      const data = await api.get<Host[]>("/api/hosts");
      setHosts(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch hosts");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchHosts();
  }, [fetchHosts]);

  const createHost = async (data: CreateHostRequest) => {
    const host = await api.post<Host>("/api/hosts", data);
    setHosts((prev) => [...prev, host]);
    return host;
  };

  const updateHost = async (id: string, data: UpdateHostRequest) => {
    const host = await api.put<Host>(`/api/hosts/${id}`, data);
    setHosts((prev) => prev.map((h) => (h.id === id ? host : h)));
    return host;
  };

  const deleteHost = async (id: string) => {
    await api.delete(`/api/hosts/${id}`);
    setHosts((prev) => prev.filter((h) => h.id !== id));
  };

  return { hosts, loading, error, fetchHosts, createHost, updateHost, deleteHost };
}
