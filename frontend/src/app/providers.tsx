"use client";

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { initializeAuth, logout } from "@/lib/auth";
import { onAuthUnauthorized } from "@/lib/authEvents";
import { useAuthStore } from "@/stores/authStore";

export function Providers({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const router = useRouter();
  const pathname = usePathname();

  const isPublicRoute = (path: string) =>
    path === "/" || path.startsWith("/login") || path.startsWith("/register");

  useEffect(() => {
    let active = true;

    const runInit = async () => {
      const authenticated = await initializeAuth();
      if (!authenticated && !isPublicRoute(pathname)) {
        router.push("/login");
      }
    };

    runInit().finally(() => {
      if (active) setReady(true);
    });

    const unsubscribe = onAuthUnauthorized(() => {
      logout();
      useAuthStore.getState().clearAuth();
      if (!isPublicRoute(pathname)) {
        router.push("/login");
      }
    });

    const handleVisibility = () => {
      if (document.visibilityState !== "visible") return;
      runInit();
    };

    window.addEventListener("focus", handleVisibility);
    document.addEventListener("visibilitychange", handleVisibility);

    return () => {
      active = false;
      unsubscribe();
      window.removeEventListener("focus", handleVisibility);
      document.removeEventListener("visibilitychange", handleVisibility);
    };
  }, [pathname, router]);

  if (!ready) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="text-sm text-[var(--muted-foreground)]">Loading...</div>
      </div>
    );
  }

  return <>{children}</>;
}
