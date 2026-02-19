export enum HostType {
  SSH = "SSH",
}

export enum HealthStatus {
  Healthy = "Healthy",
  Degraded = "Degraded",
  Unreachable = "Unreachable",
  Unknown = "Unknown",
}

export interface Host {
  id: string;
  organizationId: string;
  name: string;
  description: string | null;
  hostname: string;
  port: number;
  type: HostType;
  username: string | null;
  environment: string | null;
  tags: string[];
  status: HealthStatus;
  lastHealthCheck: string | null;
  credentialId: string | null;
  allowInsecureSsl: boolean;
  createdAt: string;
  createdBy: string;
}

export interface CreateHostRequest {
  name: string;
  description?: string;
  hostname: string;
  port: number;
  type: HostType;
  username?: string;
  environment?: string;
  tags: string[];
  credentialId?: string;
  allowInsecureSsl?: boolean;
}

export type UpdateHostRequest = Partial<CreateHostRequest>;
