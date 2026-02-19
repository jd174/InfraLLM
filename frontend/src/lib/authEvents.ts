export const AUTH_UNAUTHORIZED_EVENT = "infrallm:auth:unauthorized";

export function emitAuthUnauthorized(): void {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new Event(AUTH_UNAUTHORIZED_EVENT));
}

export function onAuthUnauthorized(handler: () => void): () => void {
  if (typeof window === "undefined") return () => undefined;
  const listener = () => handler();
  window.addEventListener(AUTH_UNAUTHORIZED_EVENT, listener);
  return () => window.removeEventListener(AUTH_UNAUTHORIZED_EVENT, listener);
}