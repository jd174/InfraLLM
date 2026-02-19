export type McpTransportType = "Http" | "Stdio";

export interface McpServer {
  id: string;
  organizationId: string;
  name: string;
  description?: string;
  transportType: McpTransportType;
  // HTTP transport
  baseUrl?: string;
  hasApiKey: boolean;
  // Stdio transport
  command?: string;
  arguments?: string;
  workingDirectory?: string;
  environmentVariables: Record<string, string>;
  isEnabled: boolean;
  createdAt: string;
  createdBy: string;
}

export interface McpToolInfo {
  name: string;
  description: string;
}

export interface McpTestResult {
  success: boolean;
  toolCount: number;
  tools: McpToolInfo[];
  error?: string;
}

export interface CreateMcpServerRequest {
  name: string;
  description?: string;
  transportType: McpTransportType;
  baseUrl?: string;
  apiKey?: string;
  command?: string;
  arguments?: string;
  workingDirectory?: string;
  environmentVariables?: Record<string, string>;
  isEnabled?: boolean;
}

export interface UpdateMcpServerRequest {
  name?: string;
  description?: string;
  transportType?: McpTransportType;
  baseUrl?: string;
  apiKey?: string;
  clearApiKey?: boolean;
  command?: string;
  arguments?: string;
  workingDirectory?: string;
  environmentVariables?: Record<string, string>;
  isEnabled?: boolean;
}
