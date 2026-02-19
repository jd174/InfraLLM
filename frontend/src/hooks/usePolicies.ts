"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Policy, CreatePolicyRequest, UpdatePolicyRequest, PolicyTestResult } from "@/types";

export function usePolicies() {
  const [policies, setPolicies] = useState<Policy[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchPolicies = useCallback(async () => {
    try {
      setLoading(true);
      const data = await api.get<Policy[]>("/api/policies");
      setPolicies(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch policies");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchPolicies();
  }, [fetchPolicies]);

  const createPolicy = async (data: CreatePolicyRequest) => {
    const policy = await api.post<Policy>("/api/policies", data);
    setPolicies((prev) => [...prev, policy]);
    return policy;
  };

  const updatePolicy = async (id: string, data: UpdatePolicyRequest) => {
    const policy = await api.put<Policy>(`/api/policies/${id}`, data);
    setPolicies((prev) => prev.map((p) => (p.id === id ? policy : p)));
    return policy;
  };

  const deletePolicy = async (id: string) => {
    await api.delete(`/api/policies/${id}`);
    setPolicies((prev) => prev.filter((p) => p.id !== id));
  };

  const testPolicy = async (id: string, command: string) => {
    return api.post<PolicyTestResult>(`/api/policies/${id}/test`, { command });
  };

  return { policies, loading, error, fetchPolicies, createPolicy, updatePolicy, deletePolicy, testPolicy };
}
