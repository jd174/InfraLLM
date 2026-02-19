import { emitAuthUnauthorized } from "./authEvents";

// With nginx reverse proxy, API and frontend share the same origin.
// All /api/* requests are proxied to the backend automatically.
const API_BASE = "";

class ApiClient {
  private token: string | null = null;

  setToken(token: string | null) {
    this.token = token;
  }

  private async request<T>(
    path: string,
    options: RequestInit = {}
  ): Promise<T> {
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
      ...((options.headers as Record<string, string>) || {}),
    };

    if (this.token) {
      headers["Authorization"] = `Bearer ${this.token}`;
    }

    const response = await fetch(`${API_BASE}${path}`, {
      ...options,
      headers,
    });

    if (!response.ok) {
      if (response.status === 401) {
        emitAuthUnauthorized();
      }
      const rawText = await response.text();
      let parsed: { error?: string; message?: string; code?: string } | null = null;
      try {
        parsed = rawText ? JSON.parse(rawText) : null;
      } catch {
        parsed = null;
      }

      const message =
        parsed?.error ||
        parsed?.message ||
        (rawText ? rawText.slice(0, 200) : response.statusText);

      throw new ApiError(response.status, message || response.statusText, parsed?.code || "UNKNOWN_ERROR");
    }

    if (response.status === 204) return undefined as T;
    
    const text = await response.text();
    if (!text) return undefined as T;
    
    return JSON.parse(text);
  }

  get<T>(path: string): Promise<T> {
    return this.request<T>(path);
  }

  post<T>(path: string, data?: unknown): Promise<T> {
    return this.request<T>(path, {
      method: "POST",
      body: JSON.stringify(data ?? {}),
    });
  }

  put<T>(path: string, data?: unknown): Promise<T> {
    return this.request<T>(path, {
      method: "PUT",
      body: JSON.stringify(data ?? {}),
    });
  }

  delete<T>(path: string): Promise<T> {
    return this.request<T>(path, { method: "DELETE" });
  }
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
    public code?: string
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export const api = new ApiClient();
