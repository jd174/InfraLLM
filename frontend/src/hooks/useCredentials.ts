"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Credential, CreateCredentialRequest } from "@/types";

export function useCredentials() {
  const [credentials, setCredentials] = useState<Credential[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchCredentials = useCallback(async () => {
    try {
      setLoading(true);
      const data = await api.get<Credential[]>("/api/credentials");
      setCredentials(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch credentials");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchCredentials();
  }, [fetchCredentials]);

  const createCredential = async (data: CreateCredentialRequest) => {
    const credential = await api.post<Credential>("/api/credentials", data);
    setCredentials((prev) => [...prev, credential]);
    return credential;
  };

  const deleteCredential = async (id: string) => {
    await api.delete(`/api/credentials/${id}`);
    setCredentials((prev) => prev.filter((c) => c.id !== id));
  };

  return { credentials, loading, error, fetchCredentials, createCredential, deleteCredential };
}
