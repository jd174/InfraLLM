"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type { Job, JobRun, CreateJobRequest, UpdateJobRequest } from "@/types";

export interface JobRunSummary {
  id: string;
  jobId: string;
  jobName: string;
  sessionId?: string | null;
  triggeredBy: string;
  status: string;
  createdAt: string;
  response?: string | null;
}

export function useJobs() {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchJobs = useCallback(async () => {
    try {
      setLoading(true);
      const data = await api.get<Job[]>("/api/jobs");
      setJobs(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch jobs");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchJobs();
  }, [fetchJobs]);

  const createJob = async (data: CreateJobRequest) => {
    const job = await api.post<Job>("/api/jobs", data);
    setJobs((prev) => [job, ...prev]);
    return job;
  };

  const updateJob = async (id: string, data: UpdateJobRequest) => {
    const job = await api.put<Job>(`/api/jobs/${id}`, data);
    setJobs((prev) => prev.map((j) => (j.id === id ? job : j)));
    return job;
  };

  const deleteJob = async (id: string) => {
    await api.delete(`/api/jobs/${id}`);
    setJobs((prev) => prev.filter((j) => j.id !== id));
  };

  const fetchRuns = async (id: string) => {
    return await api.get<JobRun[]>(`/api/jobs/${id}/runs`);
  };

  const fetchRecentRuns = async (limit = 50) => {
    return await api.get<JobRunSummary[]>(`/api/jobs/runs?limit=${limit}`);
  };

  return { jobs, loading, error, fetchJobs, createJob, updateJob, deleteJob, fetchRuns, fetchRecentRuns };
}
