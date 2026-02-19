import { api } from "./api";
import { useAuthStore } from "@/stores/authStore";
import type { AuthResponse, LoginRequest, RegisterRequest, User } from "@/types";

const TOKEN_KEY = "infrallm_token";

function decodeBase64Url(value: string): string {
  const normalized = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = normalized + "===".slice((normalized.length + 3) % 4);
  return atob(padded);
}

function getTokenExpiryMs(token: string): number | null {
  const parts = token.split(".");
  if (parts.length < 2) return null;

  try {
    const payload = JSON.parse(decodeBase64Url(parts[1]));
    if (typeof payload?.exp !== "number") return null;
    return payload.exp * 1000;
  } catch {
    return null;
  }
}

export function isTokenExpired(token: string, skewMs = 30_000): boolean {
  const expiryMs = getTokenExpiryMs(token);
  if (!expiryMs) return false;
  return Date.now() >= expiryMs - skewMs;
}

export function getStoredToken(): string | null {
  if (typeof window === "undefined") return null;
  return localStorage.getItem(TOKEN_KEY);
}

export function setStoredToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
  api.setToken(token);
}

export function clearStoredToken(): void {
  localStorage.removeItem(TOKEN_KEY);
  api.setToken(null);
}

export async function login(data: LoginRequest): Promise<AuthResponse> {
  const response = await api.post<AuthResponse>("/api/auth/login", data);
  setStoredToken(response.token);
  return response;
}

export async function register(data: RegisterRequest): Promise<AuthResponse> {
  const response = await api.post<AuthResponse>("/api/auth/register", data);
  setStoredToken(response.token);
  return response;
}

export function logout(): void {
  clearStoredToken();
}

export async function initializeAuth(): Promise<boolean> {
  const token = getStoredToken();
  if (!token) {
    useAuthStore.getState().clearAuth();
    return false;
  }

  if (isTokenExpired(token)) {
    clearStoredToken();
    useAuthStore.getState().clearAuth();
    return false;
  }

  api.setToken(token);

  try {
    // Restore user state from the /me endpoint using the stored JWT
    const user = await api.get<User>("/api/auth/me");
    useAuthStore.getState().setAuth(user, token);
    return true;
  } catch {
    // Token is expired or invalid — clear it
    clearStoredToken();
    useAuthStore.getState().clearAuth();
    return false;
  }
}
