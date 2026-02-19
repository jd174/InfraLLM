"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import { api } from "@/lib/api";
import { createChatHub } from "@/lib/signalr";
import { useAuthStore } from "@/stores/authStore";
import type { Session, Message, SendMessageRequest, CreateSessionRequest } from "@/types";

export function useSessions() {
  const [sessions, setSessions] = useState<Session[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const token = useAuthStore((state) => state.token);

  const sortSessions = useCallback((items: Session[]) => {
    return [...items].sort((a, b) => {
      const aTime = a.lastMessageAt ?? a.createdAt;
      const bTime = b.lastMessageAt ?? b.createdAt;
      return bTime.localeCompare(aTime);
    });
  }, []);

  const fetchSessions = useCallback(async () => {
    try {
      setLoading(true);
      const data = await api.get<Session[]>("/api/sessions");
      setSessions(sortSessions(data));
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch sessions");
    } finally {
      setLoading(false);
    }
  }, [sortSessions]);

  useEffect(() => {
    fetchSessions();
  }, [fetchSessions]);

  useEffect(() => {
    let isMounted = true;

    const startConnection = async () => {
      if (!token) return;

      try {
        const connection = createChatHub(() => useAuthStore.getState().token);
        connectionRef.current = connection;

        connection.on(
          "SessionUpdated",
          (payload: { sessionId: string; title?: string | null; lastMessageAt?: string | null }) => {
          if (!isMounted) return;
          setSessions((prev) =>
            sortSessions(
              prev.map((s) =>
                s.id === payload.sessionId
                  ? {
                      ...s,
                      title: payload.title ?? s.title,
                      lastMessageAt: payload.lastMessageAt ?? s.lastMessageAt,
                    }
                  : s
              )
            )
          );
        });

        await connection.start();
      } catch {
        if (!isMounted) return;
      }
    };

    startConnection();

    return () => {
      isMounted = false;
      if (connectionRef.current) {
        const connection = connectionRef.current;
        connectionRef.current = null;
        connection.stop().catch(() => undefined);
      }
    };
  }, [sortSessions, token]);

  const createSession = async (data?: CreateSessionRequest) => {
    const session = await api.post<Session>("/api/sessions", data ?? {});
    setSessions((prev) => sortSessions([session, ...prev]));
    return session;
  };

  const deleteSession = async (id: string) => {
    await api.delete(`/api/sessions/${id}`);
    setSessions((prev) => sortSessions(prev.filter((s) => s.id !== id)));
  };

  return { sessions, loading, error, fetchSessions, createSession, deleteSession };
}

export function useMessages(sessionId: string | null) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [loading, setLoading] = useState(false);
  const [sending, setSending] = useState(false);
  const [assistantTyping, setAssistantTyping] = useState(false);
  const [assistantStatus, setAssistantStatus] = useState<string | null>(null);
  const [streamConnected, setStreamConnected] = useState(false);
  const streamConnectedRef = useRef(false);
  const connectionRef = useRef<HubConnection | null>(null);
  const streamedMessageIdsRef = useRef<Set<string>>(new Set());
  const fallbackCompletedIdsRef = useRef<Set<string>>(new Set());
  const token = useAuthStore((state) => state.token);

  const fetchMessages = useCallback(async () => {
    if (!sessionId) return;
    try {
      setLoading(true);
      const data = await api.get<Message[]>(`/api/sessions/${sessionId}/messages`);
      setMessages(data);
    } catch {
      // ignore fetch errors
    } finally {
      setLoading(false);
    }
  }, [sessionId]);

  useEffect(() => {
    fetchMessages();
  }, [fetchMessages]);

  useEffect(() => {
    let isMounted = true;

    const startConnection = async () => {
      if (!sessionId || !token) return;

      try {
        const connection = createChatHub(() => useAuthStore.getState().token);
        connectionRef.current = connection;

        connection.on("AssistantStarted", (payload: { sessionId: string; messageId: string; createdAt: string }) => {
          if (!isMounted || payload.sessionId !== sessionId) return;
          setMessages((prev) => {
            if (prev.some((m) => m.id === payload.messageId)) return prev;
            return [
              ...prev,
              {
                id: payload.messageId,
                sessionId,
                role: "assistant",
                content: "",
                toolCallsJson: null,
                tokensUsed: null,
                createdAt: payload.createdAt,
              },
            ];
          });
        });

        connection.on("AssistantDelta", (payload: { sessionId: string; messageId: string; delta: string }) => {
          if (!isMounted || payload.sessionId !== sessionId) return;
          if (fallbackCompletedIdsRef.current.has(payload.messageId)) return;
          streamedMessageIdsRef.current.add(payload.messageId);
          setMessages((prev) =>
            prev.map((m) =>
              m.id === payload.messageId
                ? { ...m, content: `${m.content}${payload.delta}` }
                : m
            )
          );
        });

        connection.on(
          "AssistantCompleted",
          (payload: { sessionId: string; messageId: string; content: string; tokensUsed: number }) => {
            if (!isMounted || payload.sessionId !== sessionId) return;
            streamedMessageIdsRef.current.add(payload.messageId);
            setMessages((prev) =>
              prev.map((m) =>
                m.id === payload.messageId
                  ? { ...m, content: payload.content, tokensUsed: payload.tokensUsed }
                  : m
              )
            );
            setAssistantStatus(null);
          }
        );

        connection.on(
          "AssistantStatus",
          (payload: { sessionId: string; messageId: string; status: string }) => {
            if (!isMounted || payload.sessionId !== sessionId) return;
            setAssistantStatus(payload.status || null);
          }
        );

        connection.on("AssistantTyping", (isTyping: boolean) => {
          if (!isMounted) return;
          setAssistantTyping(isTyping);
          if (!isTyping) {
            setAssistantStatus(null);
          }
        });

        connection.onclose(() => {
          if (!isMounted) return;
          setStreamConnected(false);
          streamConnectedRef.current = false;
          setAssistantStatus("Stream disconnected");
        });

        connection.onreconnecting(() => {
          if (!isMounted) return;
          setStreamConnected(false);
          streamConnectedRef.current = false;
          setAssistantStatus("Reconnecting to stream...");
        });

        connection.onreconnected(() => {
          if (!isMounted) return;
          setStreamConnected(true);
          streamConnectedRef.current = true;
          setAssistantStatus(null);
        });

        await connection.start();
        await connection.invoke("JoinSession", sessionId);
        if (!isMounted) return;

        setStreamConnected(true);
        streamConnectedRef.current = true;
      } catch {
        if (!isMounted) return;
        setStreamConnected(false);
        streamConnectedRef.current = false;
      }
    };

    startConnection();

    return () => {
      isMounted = false;
      setAssistantTyping(false);
      setAssistantStatus(null);
      setStreamConnected(false);
      streamConnectedRef.current = false;
      if (connectionRef.current) {
        const connection = connectionRef.current;
        connectionRef.current = null;
        connection.invoke("LeaveSession", sessionId ?? "").catch(() => undefined);
        connection.stop().catch(() => undefined);
      }
    };
  }, [sessionId, token]);

  interface SendMessageResponse {
    userMessage: Message;
    assistantMessage: Message;
    tokensUsed: number;
    cost: number;
    streamed?: boolean;
  }

  const sendMessage = async (data: SendMessageRequest, overrideSessionId?: string) => {
    const targetSessionId = overrideSessionId || sessionId;
    if (!targetSessionId) return;
    try {
      setSending(true);

      // Optimistically add user message
      const tempUserMessage: Message = {
        id: crypto.randomUUID(),
        sessionId: targetSessionId,
        role: "user",
        content: data.content,
        toolCallsJson: null,
        tokensUsed: null,
        createdAt: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, tempUserMessage]);

      const response = await api.post<SendMessageResponse>(
        `/api/sessions/${targetSessionId}/messages`,
        data
      );
      
      // Replace temp message with actual user message
      const applyAssistantResponse = () => {
        setMessages((prev) => {
          const withoutTemp = prev.filter((m) => m.id !== tempUserMessage.id);
          const hasUserMessage = withoutTemp.some((m) => m.id === response.userMessage.id);
          const merged = hasUserMessage
            ? withoutTemp.map((m) => (m.id === response.userMessage.id ? response.userMessage : m))
            : [...withoutTemp, response.userMessage];

          const hasAssistant = merged.some((m) => m.id === response.assistantMessage.id);
          return hasAssistant
            ? merged.map((m) => (m.id === response.assistantMessage.id ? response.assistantMessage : m))
            : [...merged, response.assistantMessage];
        });
      };

      setMessages((prev) => {
        const withoutTemp = prev.filter((m) => m.id !== tempUserMessage.id);
        const hasUserMessage = withoutTemp.some((m) => m.id === response.userMessage.id);
        const merged = hasUserMessage
          ? withoutTemp.map((m) => (m.id === response.userMessage.id ? response.userMessage : m))
          : [...withoutTemp, response.userMessage];

        if (!streamConnectedRef.current) {
          const hasAssistant = merged.some((m) => m.id === response.assistantMessage.id);
          return hasAssistant
            ? merged.map((m) => (m.id === response.assistantMessage.id ? response.assistantMessage : m))
            : [...merged, response.assistantMessage];
        }

        return merged;
      });

      if (streamConnectedRef.current) {
        const assistantId = response.assistantMessage.id;
        if (!streamedMessageIdsRef.current.has(assistantId)) {
          window.setTimeout(() => {
            if (streamedMessageIdsRef.current.has(assistantId)) return;
            fallbackCompletedIdsRef.current.add(assistantId);
            applyAssistantResponse();
            setAssistantStatus("Stream failed — showing full response");
          }, 1500);
        }
      }
      
      return response;
    } catch (error) {
      // Remove optimistic message on error
      setMessages((prev) => prev.filter((m) => m.role !== "user" || m.content !== data.content));
      throw error;
    } finally {
      setSending(false);
    }
  };

  const addMessage = (message: Message) => {
    setMessages((prev) => [...prev, message]);
  };

  const cancelMessage = async () => {
    if (!sessionId) return;
    setAssistantStatus("Cancelling...");
    try {
      await api.post(`/api/sessions/${sessionId}/cancel`, {});
    } catch {
      // ignore cancel errors
    }
  };

  return {
    messages,
    loading,
    sending,
    assistantTyping,
    assistantStatus,
    streamConnected,
    fetchMessages,
    sendMessage,
    addMessage,
    cancelMessage,
  };
}
