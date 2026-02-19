export enum AuditEventType {
  CommandExecuted = "CommandExecuted",
  CommandDenied = "CommandDenied",
  HostAdded = "HostAdded",
  HostRemoved = "HostRemoved",
  PolicyChanged = "PolicyChanged",
  SessionStarted = "SessionStarted",
  SessionEnded = "SessionEnded",
}

export interface AuditLog {
  id: string;
  organizationId: string;
  userId: string;
  sessionId: string | null;
  hostId: string | null;
  hostName: string | null;
  eventType: AuditEventType;
  command: string | null;
  wasAllowed: boolean | null;
  denialReason: string | null;
  executionId: string | null;
  llmReasoning: string | null;
  timestamp: string;
  metadataJson: string | null;
}

export interface AuditSearchRequest {
  userId?: string;
  hostId?: string;
  eventType?: AuditEventType;
  startDate?: string;
  endDate?: string;
  commandContains?: string;
  page?: number;
  pageSize?: number;
}

export interface PaginatedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
