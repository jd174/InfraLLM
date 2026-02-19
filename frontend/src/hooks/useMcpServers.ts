"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import type {
  McpServer,
  CreateMcpServerRequest,
  UpdateMcpServerRequest,
  McpTestResult,
  McpToolInfo,
} from "@/types";

export function useMcpServers() {
  const [servers, setServers] = useState<McpServer[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchServers = useCallback(async () => {
    try {
      setLoading(true);
      const data = await api.get<McpServer[]>("/api/mcp-servers");
      setServers(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch MCP servers");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchServers();
  }, [fetchServers]);

  const createServer = async (data: CreateMcpServerRequest) => {
    const server = await api.post<McpServer>("/api/mcp-servers", data);
    setServers((prev) => [...prev, server]);
    return server;
  };

  const updateServer = async (id: string, data: UpdateMcpServerRequest) => {
    const server = await api.put<McpServer>(`/api/mcp-servers/${id}`, data);
    setServers((prev) => prev.map((s) => (s.id === id ? server : s)));
    return server;
  };

  const deleteServer = async (id: string) => {
    await api.delete(`/api/mcp-servers/${id}`);
    setServers((prev) => prev.filter((s) => s.id !== id));
  };

  const testServer = async (id: string): Promise<McpTestResult> => {
    return api.post<McpTestResult>(`/api/mcp-servers/${id}/test`);
  };

  const listTools = async (id: string): Promise<McpToolInfo[]> => {
    return api.get<McpToolInfo[]>(`/api/mcp-servers/${id}/tools`);
  };

  return {
    servers,
    loading,
    error,
    fetchServers,
    createServer,
    updateServer,
    deleteServer,
    testServer,
    listTools,
  };
}
