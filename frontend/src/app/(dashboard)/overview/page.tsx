"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api } from "@/lib/api";
import { formatDate } from "@/lib/utils";
import type { AuditLog, PaginatedResult } from "@/types";
import type { Host } from "@/types";
import { HealthStatus } from "@/types";
import { Card, CardContent, CardHeader } from "@/components/ui/Card";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import {
  HostsIcon,
  TokenIcon,
  AuditIcon,
  CheckIcon,
  CopyIcon,
  ChevronRightIcon,
} from "@/components/ui/Icons";

interface AccessToken {
  id: string;
  name: string;
  expiresAt: string | null;
  isActive: boolean;
}

type ClientTab = "claude-code" | "claude-desktop" | "cursor";

const eventBadgeVariant = (log: AuditLog): "success" | "danger" | "default" | "neutral" => {
  if (log.eventType === "CommandDenied" || log.wasAllowed === false) return "danger";
  if (log.eventType === "CommandExecuted") return "success";
  if (log.eventType === "PolicyChanged") return "default";
  return "neutral";
};

function CopyBlock({ text, label }: { text: string; label?: string }) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // ignore
    }
  };

  return (
    <div className="relative group">
      <pre className="text-xs font-mono text-foreground bg-muted/50 border border-border rounded-lg p-3 overflow-x-auto whitespace-pre">
        {text}
      </pre>
      <button
        onClick={handleCopy}
        className="absolute top-2 right-2 p-1.5 rounded bg-background/80 text-muted-foreground hover:text-foreground border border-border transition"
        title={label ?? "Copy to clipboard"}
      >
        {copied ? <CheckIcon size={14} /> : <CopyIcon size={14} />}
      </button>
    </div>
  );
}

function ConnectClientCard({ hasActiveToken }: { hasActiveToken: boolean }) {
  const [tab, setTab] = useState<ClientTab>("claude-code");

  const baseUrl =
    typeof window !== "undefined"
      ? `${window.location.protocol}//${window.location.host}`
      : "https://your-infrallm-instance";
  const sseUrl = `${baseUrl}/mcp/sse`;

  const snippets: Record<ClientTab, { title: string; hint: string; code: string }> = {
    "claude-code": {
      title: "Claude Code",
      hint: "Run in your terminal:",
      code: `claude mcp add --transport sse infrallm ${sseUrl} \\\n  --header "Authorization: Bearer infra_YOUR_TOKEN"`,
    },
    "claude-desktop": {
      title: "Claude Desktop",
      hint: "Add to claude_desktop_config.json:",
      code: `{\n  "mcpServers": {\n    "infrallm": {\n      "command": "npx",\n      "args": [\n        "-y", "mcp-remote", "${sseUrl}",\n        "--header", "Authorization:Bearer infra_YOUR_TOKEN"\n      ]\n    }\n  }\n}`,
    },
    cursor: {
      title: "Cursor",
      hint: "Add to .cursor/mcp.json:",
      code: `{\n  "mcpServers": {\n    "infrallm": {\n      "url": "${sseUrl}",\n      "headers": {\n        "Authorization": "Bearer infra_YOUR_TOKEN"\n      }\n    }\n  }\n}`,
    },
  };

  const active = snippets[tab];

  return (
    <Card>
      <CardHeader>
        <h2 className="text-sm font-semibold text-foreground">Connect your AI client</h2>
        <p className="text-xs text-muted-foreground mt-0.5">
          InfraLLM is an MCP server — point any MCP-compatible client at it to manage your
          hosts with policy enforcement and full audit logging.
        </p>
      </CardHeader>
      <CardContent className="space-y-3">
        {!hasActiveToken && (
          <div className="rounded-lg border border-yellow-500/30 bg-yellow-500/5 px-3 py-2.5 text-xs text-yellow-400">
            You need an access token first —{" "}
            <Link href="/access-tokens" className="underline hover:text-foreground">
              create one here
            </Link>
            , then substitute it for <code className="font-mono">infra_YOUR_TOKEN</code> below.
          </div>
        )}

        <div className="flex gap-1.5">
          {(Object.keys(snippets) as ClientTab[]).map((key) => (
            <button
              key={key}
              onClick={() => setTab(key)}
              className={
                tab === key
                  ? "px-3 py-1.5 rounded-lg text-xs font-medium bg-accent text-accent-foreground transition"
                  : "px-3 py-1.5 rounded-lg text-xs font-medium text-muted-foreground hover:bg-muted hover:text-foreground transition"
              }
            >
              {snippets[key].title}
            </button>
          ))}
        </div>

        <div>
          <p className="text-xs text-muted-foreground mb-1.5">{active.hint}</p>
          <CopyBlock text={active.code} />
        </div>

        <p className="text-xs text-muted-foreground">
          Endpoint details, auth options, and the full tool list are on the{" "}
          <Link href="/access-tokens" className="underline hover:text-foreground">
            Access Tokens
          </Link>{" "}
          page.
        </p>
      </CardContent>
    </Card>
  );
}

function StatCard({
  href,
  label,
  value,
  detail,
  Icon,
}: {
  href: string;
  label: string;
  value: string;
  detail: string;
  Icon: React.ComponentType<{ size?: number; className?: string }>;
}) {
  return (
    <Link href={href} className="block">
      <Card className="hover:border-accent transition h-full">
        <CardContent className="p-4">
          <div className="flex items-center gap-2 text-muted-foreground mb-2">
            <Icon size={15} />
            <span className="text-xs font-medium">{label}</span>
          </div>
          <p className="text-2xl font-semibold text-foreground">{value}</p>
          <p className="text-xs text-muted-foreground mt-0.5">{detail}</p>
        </CardContent>
      </Card>
    </Link>
  );
}

export default function OverviewPage() {
  const [hosts, setHosts] = useState<Host[]>([]);
  const [tokens, setTokens] = useState<AccessToken[]>([]);
  const [recentLogs, setRecentLogs] = useState<AuditLog[]>([]);
  const [totalEvents, setTotalEvents] = useState(0);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    Promise.allSettled([
      api.get<Host[]>("/api/hosts"),
      api.get<AccessToken[]>("/api/access-tokens"),
      api.get<PaginatedResult<AuditLog>>("/api/audit?page=1&pageSize=8"),
    ]).then(([hostsRes, tokensRes, auditRes]) => {
      if (cancelled) return;
      if (hostsRes.status === "fulfilled") setHosts(hostsRes.value);
      if (tokensRes.status === "fulfilled") setTokens(tokensRes.value);
      if (auditRes.status === "fulfilled") {
        setRecentLogs(auditRes.value.items);
        setTotalEvents(auditRes.value.totalCount);
      }
      setLoading(false);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  const healthyHosts = hosts.filter((h) => h.status === HealthStatus.Healthy).length;
  const activeTokens = tokens.filter(
    (t) => t.isActive && (!t.expiresAt || new Date(t.expiresAt) > new Date())
  ).length;

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading overview...</p>
      </div>
    );
  }

  const isEmpty = hosts.length === 0;

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto space-y-6">
      <div>
        <h1 className="text-lg font-semibold text-foreground">Overview</h1>
        <p className="text-sm text-muted-foreground">
          Your MCP gateway to {hosts.length === 1 ? "1 host" : `${hosts.length} hosts`}
        </p>
      </div>

      {/* Getting started — shown until there's at least one host */}
      {isEmpty && (
        <Card>
          <CardHeader>
            <h2 className="text-sm font-semibold text-foreground">Get started</h2>
          </CardHeader>
          <CardContent>
            <ol className="space-y-2.5">
              {[
                { done: hosts.length > 0, label: "Add a host with SSH credentials", href: "/hosts" },
                { done: activeTokens > 0, label: "Create an access token", href: "/access-tokens" },
                { done: false, label: "Connect your AI client using a config below", href: null },
                { done: false, label: "Optionally scope commands with policies", href: "/policies" },
              ].map((step, i) => (
                <li key={i} className="flex items-center gap-2.5 text-sm">
                  <span
                    className={
                      step.done
                        ? "flex items-center justify-center w-5 h-5 rounded-full bg-green-500/15 text-green-400 shrink-0"
                        : "flex items-center justify-center w-5 h-5 rounded-full border border-border text-muted-foreground text-xs shrink-0"
                    }
                  >
                    {step.done ? <CheckIcon size={12} /> : i + 1}
                  </span>
                  {step.href ? (
                    <Link href={step.href} className="text-foreground hover:underline">
                      {step.label}
                    </Link>
                  ) : (
                    <span className="text-foreground">{step.label}</span>
                  )}
                </li>
              ))}
            </ol>
          </CardContent>
        </Card>
      )}

      {/* Stats */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
        <StatCard
          href="/hosts"
          label="Hosts"
          value={String(hosts.length)}
          detail={hosts.length > 0 ? `${healthyHosts} healthy` : "None yet"}
          Icon={HostsIcon}
        />
        <StatCard
          href="/access-tokens"
          label="Access Tokens"
          value={String(activeTokens)}
          detail={activeTokens > 0 ? "active" : "Create one to connect"}
          Icon={TokenIcon}
        />
        <StatCard
          href="/audit"
          label="Audit Events"
          value={String(totalEvents)}
          detail="all time"
          Icon={AuditIcon}
        />
      </div>

      {/* Connect card */}
      <ConnectClientCard hasActiveToken={activeTokens > 0} />

      {/* Recent activity */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold text-foreground">Recent activity</h2>
            <Link href="/audit">
              <Button variant="ghost" size="sm">
                View all <ChevronRightIcon size={14} />
              </Button>
            </Link>
          </div>
        </CardHeader>
        <CardContent className="p-0">
          {recentLogs.length === 0 ? (
            <p className="text-sm text-muted-foreground px-4 py-6 text-center">
              No activity yet. Events appear here once a client starts making tool calls.
            </p>
          ) : (
            <div className="divide-y divide-border">
              {recentLogs.map((log) => (
                <div key={log.id} className="flex items-center gap-3 px-4 py-2.5">
                  <Badge variant={eventBadgeVariant(log)}>{log.eventType}</Badge>
                  <div className="flex-1 min-w-0">
                    {log.command ? (
                      <code className="text-xs font-mono text-foreground truncate block">
                        {log.command}
                      </code>
                    ) : (
                      <span className="text-xs text-muted-foreground">
                        {log.hostName ?? "—"}
                      </span>
                    )}
                  </div>
                  {log.hostName && log.command && (
                    <span className="text-xs text-muted-foreground shrink-0 hidden sm:block">
                      {log.hostName}
                    </span>
                  )}
                  <span className="text-xs text-muted-foreground shrink-0">
                    {formatDate(log.timestamp)}
                  </span>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
