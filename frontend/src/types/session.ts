export interface Session {
  id: string;
  organizationId: string;
  userId: string;
  title: string | null;
  hostIds: string[];
  createdAt: string;
  lastMessageAt: string | null;
  isJobRunSession: boolean;
  totalTokens: number;
  totalCost: number;
}

export interface Message {
  id: string;
  sessionId: string;
  role: "user" | "assistant";
  content: string;
  toolCallsJson: string | null;
  tokensUsed: number | null;
  createdAt: string;
}

export interface SendMessageRequest {
  content: string;
  hostIds?: string[];
  model?: string;
}

export interface CreateSessionRequest {
  title?: string;
  hostIds?: string[];
}

export interface ToolCall {
  id: string;
  name: string;
  arguments: Record<string, unknown>;
  result?: string;
}
