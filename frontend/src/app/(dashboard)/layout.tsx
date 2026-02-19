"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useAuthStore } from "@/stores/authStore";
import { useUIStore } from "@/stores/uiStore";
import { logout } from "@/lib/auth";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";
import {
  ChatIcon,
  JobRunsIcon,
  JobsIcon,
  HostsIcon,
  // McpIcon,
  CredentialsIcon,
  PoliciesIcon,
  AuditIcon,
  LogoutIcon,
  MenuIcon,
  XIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  SettingsIcon,
} from "@/components/ui/Icons";

const navItems = [
  { href: "/chat", label: "Chat", Icon: ChatIcon },
  { href: "/job-runs", label: "Job Runs", Icon: JobRunsIcon },
  { href: "/jobs", label: "Jobs", Icon: JobsIcon },
  { href: "/hosts", label: "Hosts", Icon: HostsIcon },
  // { href: "/mcp-servers", label: "MCP Servers", Icon: McpIcon },
  { href: "/credentials", label: "Credentials", Icon: CredentialsIcon },
  { href: "/policies", label: "Policies", Icon: PoliciesIcon },
  { href: "/audit", label: "Audit Log", Icon: AuditIcon },
  { href: "/settings", label: "Settings", Icon: SettingsIcon },
];

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const { user, isAuthenticated } = useAuthStore();
  const { sidebarOpen, toggleSidebar } = useUIStore();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  useEffect(() => {
    if (!isAuthenticated) router.push("/login");
  }, [isAuthenticated, router]);

  const handleLogout = () => {
    logout();
    useAuthStore.getState().clearAuth();
    router.push("/login");
  };

  if (!isAuthenticated) return null;

  return (
    <div className="flex h-dvh bg-background">
      {/* Desktop sidebar */}
      <aside
        className={cn(
          "hidden md:flex flex-col border-r border-border transition-all duration-200 shrink-0",
          sidebarOpen ? "w-56" : "w-14"
        )}
      >
        {/* Sidebar header */}
        <div className="flex items-center justify-between px-3 py-3 border-b border-border h-14">
          {sidebarOpen && (
            <span className="text-sm font-semibold tracking-tight text-foreground">InfraLLM</span>
          )}
          <button
            onClick={toggleSidebar}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted transition ml-auto"
            aria-label={sidebarOpen ? "Collapse sidebar" : "Expand sidebar"}
          >
            {sidebarOpen ? <ChevronLeftIcon size={16} /> : <ChevronRightIcon size={16} />}
          </button>
        </div>

        {/* Nav */}
        <nav className="flex-1 p-2 space-y-0.5 overflow-y-auto">
          {navItems.map(({ href, label, Icon }) => {
            const active = pathname.startsWith(href);
            return (
              <Link
                key={href}
                href={href}
                title={!sidebarOpen ? label : undefined}
                className={cn(
                  "flex items-center gap-2.5 px-2.5 py-2 rounded-lg text-sm transition",
                  active
                    ? "bg-accent text-accent-foreground"
                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                )}
              >
                <Icon size={16} className="shrink-0" />
                {sidebarOpen && <span className="truncate">{label}</span>}
              </Link>
            );
          })}
        </nav>

        {/* Footer */}
        <div className="p-2 border-t border-border">
          {sidebarOpen && (
            <p className="text-xs text-muted-foreground px-2.5 mb-1 truncate">
              {user?.displayName || user?.email}
            </p>
          )}
          <button
            onClick={handleLogout}
            title={!sidebarOpen ? "Sign out" : undefined}
            className="flex items-center gap-2.5 w-full px-2.5 py-2 rounded-lg text-sm text-muted-foreground hover:text-destructive hover:bg-muted transition"
          >
            <LogoutIcon size={16} className="shrink-0" />
            {sidebarOpen && <span>Sign out</span>}
          </button>
        </div>
      </aside>

      {/* Mobile overlay */}
      {mobileMenuOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/60 md:hidden"
          onClick={() => setMobileMenuOpen(false)}
        />
      )}

      {/* Mobile drawer */}
      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-50 w-64 flex flex-col bg-background border-r border-border",
          "md:hidden transition-transform duration-200",
          mobileMenuOpen ? "translate-x-0" : "-translate-x-full"
        )}
      >
        <div className="flex items-center justify-between px-4 py-3 border-b border-border h-14">
          <span className="text-sm font-semibold tracking-tight">InfraLLM</span>
          <button
            onClick={() => setMobileMenuOpen(false)}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted transition"
            aria-label="Close menu"
          >
            <XIcon size={16} />
          </button>
        </div>

        <nav className="flex-1 p-2 space-y-0.5 overflow-y-auto">
          {navItems.map(({ href, label, Icon }) => {
            const active = pathname.startsWith(href);
            return (
              <Link
                key={href}
                href={href}
                onClick={() => setMobileMenuOpen(false)}
                className={cn(
                  "flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition",
                  active
                    ? "bg-accent text-accent-foreground"
                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                )}
              >
                <Icon size={16} />
                <span>{label}</span>
              </Link>
            );
          })}
        </nav>

        <div className="p-2 border-t border-border">
          <p className="text-xs text-muted-foreground px-3 mb-1 truncate">
            {user?.displayName || user?.email}
          </p>
          <button
            onClick={handleLogout}
            className="flex items-center gap-3 w-full px-3 py-2.5 rounded-lg text-sm text-muted-foreground hover:text-destructive hover:bg-muted transition"
          >
            <LogoutIcon size={16} />
            <span>Sign out</span>
          </button>
        </div>
      </aside>

      {/* Main content */}
      <div className="flex flex-col flex-1 min-w-0">
        {/* Mobile top bar */}
        <header className="md:hidden flex items-center gap-3 px-4 h-14 border-b border-border shrink-0">
          <button
            onClick={() => setMobileMenuOpen(true)}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-muted transition"
            aria-label="Open menu"
          >
            <MenuIcon size={18} />
          </button>
          <span className="text-sm font-semibold">InfraLLM</span>
        </header>

        <main className="flex-1 overflow-auto">{children}</main>
      </div>
    </div>
  );
}
