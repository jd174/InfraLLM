"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useRouter, useSearchParams } from "next/navigation";
import { useSessions, useMessages } from "@/hooks/useSession";
import { useHosts } from "@/hooks/useHosts";
import { usePromptSettings } from "@/hooks/usePromptSettings";
import { useFeatures } from "@/hooks/useFeatures";
import { formatRelativeTime } from "@/lib/utils";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import {
  PlusIcon,
  XIcon,
  SendIcon,
  MenuIcon,
  ChevronLeftIcon,
} from "@/components/ui/Icons";

type SessionListProps = {
  sessionSearch: string;
  filteredSessions: ReturnType<typeof useSessions>["sessions"];
  activeSessionId: string | null;
  onSessionSearchChange: (value: string) => void;
  onNewSession: () => void;
  onSelectSession: (sessionId: string, hostIds: string[] | null | undefined) => void;
  onDeleteSession: (sessionId: string) => void;
};

function SessionList({
  sessionSearch,
  filteredSessions,
  activeSessionId,
  onSessionSearchChange,
  onNewSession,
  onSelectSession,
  onDeleteSession,
}: SessionListProps) {
  return (
    <div className="flex flex-col h-full">
      <div className="p-3 border-b border-border">
        <Button onClick={onNewSession} className="w-full" size="sm">
          <PlusIcon size={14} /> New Chat
        </Button>
      </div>
      <div className="p-3 border-b border-border">
        <Input
          value={sessionSearch}
          onChange={(e) => onSessionSearchChange(e.target.value)}
          placeholder="Search chats"
          className="text-xs"
        />
      </div>
      <div className="flex-1 overflow-y-auto p-2 space-y-0.5">
        {filteredSessions.map((session) => (
          <div
            key={session.id}
            className={cn(
              "group flex items-center gap-2 rounded-lg px-3 py-2 text-sm cursor-pointer transition",
              activeSessionId === session.id
                ? "bg-accent text-accent-foreground"
                : "text-muted-foreground hover:bg-muted hover:text-foreground"
            )}
            onClick={() => onSelectSession(session.id, session.hostIds)}
          >
            <div className="truncate flex-1 text-xs">{session.title || "New Chat"}</div>
            <div className="flex items-center gap-1 shrink-0">
              <span className="text-[10px] opacity-60">
                {session.lastMessageAt ? formatRelativeTime(session.lastMessageAt) : ""}
              </span>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  onDeleteSession(session.id);
                }}
                className="opacity-0 group-hover:opacity-100 p-0.5 rounded text-muted-foreground hover:text-destructive transition"
              >
                <XIcon size={12} />
              </button>
            </div>
          </div>
        ))}
        {filteredSessions.length === 0 && (
          <p className="text-xs text-muted-foreground text-center py-4">No chats yet</p>
        )}
      </div>
    </div>
  );
}

export default function ChatPage() {
  const { chatEnabled } = useFeatures();
  const { sessions, createSession, deleteSession } = useSessions();
  const searchParams = useSearchParams();
  const router = useRouter();
  const { hosts } = useHosts();
  const { settings } = usePromptSettings();
  const activeSessionId = searchParams.get("sessionId");
  const { messages, sending, assistantTyping, assistantStatus, sendMessage, cancelMessage } = useMessages(activeSessionId);
  const [input, setInput] = useState("");
  const [selectedHostIds, setSelectedHostIds] = useState<string[]>([]);
  const [sessionSearch, setSessionSearch] = useState("");
  const [showSessionDrawer, setShowSessionDrawer] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const orderedMessages = useMemo(() => {
    return [...messages].sort((a, b) => {
      const timeA = new Date(a.createdAt).getTime();
      const timeB = new Date(b.createdAt).getTime();
      if (timeA !== timeB) return timeA - timeB;
      if (a.role === b.role) return 0;
      return a.role === "user" ? -1 : 1;
    });
  }, [messages]);

  const inFlight = sending || assistantTyping;

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  const activeSession = useMemo(
    () => sessions.find((s) => s.id === activeSessionId) ?? null,
    [activeSessionId, sessions]
  );

  const effectiveSelectedHostIds = useMemo(() => {
    return selectedHostIds.length > 0 ? selectedHostIds : activeSession?.hostIds ?? [];
  }, [activeSession?.hostIds, selectedHostIds]);

  const filteredSessions = useMemo(() => {
    const visibleSessions = sessions.filter((s) => !s.isJobRunSession);
    const term = sessionSearch.trim().toLowerCase();
    if (!term) return visibleSessions;
    return visibleSessions.filter((s) => (s.title ?? "").toLowerCase().includes(term));
  }, [sessionSearch, sessions]);

  const handleNewSession = async () => {
    const session = await createSession({ hostIds: selectedHostIds });
    router.replace(`?sessionId=${session.id}`);
    setShowSessionDrawer(false);
  };

  const handleSelectSession = (sessionId: string, hostIds: string[] | null | undefined) => {
    setSelectedHostIds(hostIds ?? []);
    router.replace(`?sessionId=${sessionId}`);
    setShowSessionDrawer(false);
  };

  const handleDeleteSession = (sessionId: string) => {
    deleteSession(sessionId);
    if (activeSessionId === sessionId) router.replace("?");
  };

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || sending) return;
    const content = input.trim();
    setInput("");
    let targetSessionId = activeSessionId;
    if (!targetSessionId) {
      const session = await createSession({ hostIds: selectedHostIds });
      targetSessionId = session.id;
      router.replace(`?sessionId=${session.id}`);
    }
    await sendMessage(
      { content, hostIds: effectiveSelectedHostIds, model: settings.defaultModel || undefined },
      targetSessionId
    );
  };

  const toggleHost = (hostId: string) => {
    setSelectedHostIds((prev) => {
      const base = prev.length > 0 ? prev : activeSession?.hostIds ?? [];
      return base.includes(hostId) ? base.filter((id) => id !== hostId) : [...base, hostId];
    });
  };

  if (!chatEnabled) {
    return (
      <div className="flex h-full items-center justify-center p-6 text-center">
        <div className="space-y-3 max-w-sm">
          <h2 className="text-xl font-semibold text-foreground">Chat unavailable</h2>
          <p className="text-sm text-muted-foreground">
            The built-in AI chat requires an Anthropic API key to be configured on the server.
          </p>
          <p className="text-sm text-muted-foreground">
            You can still use InfraLLM via the MCP server from Claude Desktop or any compatible client.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full">
      {/* Desktop session sidebar */}
      <div className="hidden md:flex w-56 shrink-0 border-r border-border flex-col">
        <SessionList
          sessionSearch={sessionSearch}
          filteredSessions={filteredSessions}
          activeSessionId={activeSessionId}
          onSessionSearchChange={setSessionSearch}
          onNewSession={handleNewSession}
          onSelectSession={handleSelectSession}
          onDeleteSession={handleDeleteSession}
        />
      </div>

      {/* Mobile session drawer overlay */}
      {showSessionDrawer && (
        <div
          className="fixed inset-0 z-40 bg-black/60 md:hidden"
          onClick={() => setShowSessionDrawer(false)}
        />
      )}

      {/* Mobile session drawer */}
      <div
        className={cn(
          "fixed inset-y-0 left-0 z-50 w-64 bg-background border-r border-border flex flex-col",
          "md:hidden transition-transform duration-200",
          showSessionDrawer ? "translate-x-0" : "-translate-x-full"
        )}
      >
        <div className="flex items-center justify-between px-3 py-3 border-b border-border h-14">
          <span className="text-sm font-medium">Chats</span>
          <button
            onClick={() => setShowSessionDrawer(false)}
            className="p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted"
          >
            <ChevronLeftIcon size={16} />
          </button>
        </div>
        <SessionList
          sessionSearch={sessionSearch}
          filteredSessions={filteredSessions}
          activeSessionId={activeSessionId}
          onSessionSearchChange={setSessionSearch}
          onNewSession={handleNewSession}
          onSelectSession={handleSelectSession}
          onDeleteSession={handleDeleteSession}
        />
      </div>

      {/* Main chat area */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Toolbar */}
        <div className="border-b border-border px-3 py-2 flex flex-wrap items-center gap-2">
          {/* Mobile: session toggle button */}
          <button
            onClick={() => setShowSessionDrawer(true)}
            className="md:hidden p-1.5 rounded text-muted-foreground hover:text-foreground hover:bg-muted transition"
            aria-label="Open chats"
          >
            <MenuIcon size={16} />
          </button>

          {/* Host scope pills */}
          <div className="flex items-center gap-1.5 flex-wrap flex-1 min-w-0">
            <span className="text-xs text-muted-foreground shrink-0">Hosts:</span>
            {hosts.length === 0 ? (
              <span className="text-xs text-muted-foreground">None</span>
            ) : (
              hosts.map((host) => (
                <button
                  key={host.id}
                  type="button"
                  onClick={() => toggleHost(host.id)}
                  className={cn(
                    "rounded-full border px-2.5 py-0.5 text-xs transition",
                    effectiveSelectedHostIds.includes(host.id)
                      ? "bg-primary text-primary-foreground border-primary"
                      : "border-border text-muted-foreground hover:bg-muted"
                  )}
                >
                  {host.name}
                </button>
              ))
            )}
          </div>
        </div>

        {/* Messages or empty state */}
        {activeSessionId ? (
          <>
            <div className="flex-1 overflow-y-auto p-4 md:p-6 space-y-4">
              {orderedMessages.length === 0 && (
                <div className="flex items-center justify-center h-full text-muted-foreground">
                  <div className="text-center space-y-1">
                    <p className="font-medium">Start a conversation</p>
                    <p className="text-sm">
                      Ask me to check a server, run diagnostics, or manage services.
                    </p>
                  </div>
                </div>
              )}
              {orderedMessages.map((msg) => (
                <div
                  key={msg.id}
                  className={cn("flex", msg.role === "user" ? "justify-end" : "justify-start")}
                >
                  <div
                    className={cn(
                      "max-w-[85%] md:max-w-[75%] rounded-xl px-4 py-3 text-sm leading-relaxed",
                      msg.role === "user"
                        ? "bg-primary text-primary-foreground"
                        : "bg-card border border-border"
                    )}
                  >
                    {msg.role === "assistant" ? (
                      <div className="prose prose-sm max-w-none">
                        <ReactMarkdown remarkPlugins={[remarkGfm]}>
                          {msg.content}
                        </ReactMarkdown>
                      </div>
                    ) : (
                      <p className="whitespace-pre-wrap">{msg.content}</p>
                    )}
                    <p
                      className={cn(
                        "text-[10px] mt-1.5",
                        msg.role === "user" ? "text-primary-foreground/70" : "text-muted-foreground"
                      )}
                    >
                      {formatRelativeTime(msg.createdAt)}
                    </p>
                  </div>
                </div>
              ))}
              {(sending || assistantTyping) && (
                <div className="flex justify-start">
                  <div className="bg-card border border-border rounded-xl px-4 py-3 text-sm text-muted-foreground flex items-center gap-2">
                    <span className="animate-pulse">●</span> {assistantStatus || "Thinking..."}
                  </div>
                </div>
              )}
              <div ref={messagesEndRef} />
            </div>

            <form onSubmit={handleSend} className="border-t border-border p-3">
              <div className="flex gap-2">
                <Input
                  type="text"
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  placeholder="Ask about your infrastructure..."
                  disabled={inFlight}
                  className="flex-1"
                />
                <Button
                  type={inFlight ? "button" : "submit"}
                  onClick={inFlight ? cancelMessage : undefined}
                  disabled={inFlight ? false : !input.trim()}
                  variant={inFlight ? "secondary" : "primary"}
                  className="shrink-0"
                >
                  {inFlight ? (
                    <>
                      <XIcon size={14} />
                      <span className="hidden sm:inline">Stop</span>
                    </>
                  ) : (
                    <>
                      <SendIcon size={14} />
                      <span className="hidden sm:inline">Send</span>
                    </>
                  )}
                </Button>
              </div>
            </form>
          </>
        ) : (
          <div className="flex-1 flex items-center justify-center text-muted-foreground p-6">
            <div className="text-center space-y-3">
              <h2 className="text-xl font-semibold text-foreground">InfraLLM Chat</h2>
              <p className="text-sm">Select a conversation or start a new one</p>
              <Button onClick={handleNewSession}>
                <PlusIcon size={14} /> New Chat
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
