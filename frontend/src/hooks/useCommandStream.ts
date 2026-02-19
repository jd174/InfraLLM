"use client";

import { useCallback, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { createCommandHub } from "@/lib/signalr";
import { useAuthStore } from "@/stores/authStore";

interface CommandOutput {
  line: string;
  timestamp: Date;
}

export function useCommandStream() {
  const [output, setOutput] = useState<CommandOutput[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const token = useAuthStore((state) => state.token);

  const startStream = useCallback(
    async (hostId: string, command: string) => {
      if (!token) {
        setError("Not authenticated");
        return;
      }

      try {
        setOutput([]);
        setError(null);
        setIsStreaming(true);

        const connection = createCommandHub(() => useAuthStore.getState().token);
        connectionRef.current = connection;

        connection.on("CommandOutput", (line: string) => {
          setOutput((prev) => [...prev, { line, timestamp: new Date() }]);
        });

        connection.on("CommandCompleted", (exitCode: number) => {
          setOutput((prev) => [
            ...prev,
            { line: `\n[Process exited with code ${exitCode}]`, timestamp: new Date() },
          ]);
          setIsStreaming(false);
        });

        connection.on("CommandFailed", (errorMsg: string) => {
          setError(errorMsg);
          setIsStreaming(false);
        });

        await connection.start();
        await connection.invoke("StreamCommandOutput", hostId, command);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Stream failed");
        setIsStreaming(false);
      }
    },
    [token]
  );

  const stopStream = useCallback(async () => {
    if (connectionRef.current) {
      await connectionRef.current.stop();
      connectionRef.current = null;
      setIsStreaming(false);
    }
  }, []);

  return { output, isStreaming, error, startStream, stopStream };
}
