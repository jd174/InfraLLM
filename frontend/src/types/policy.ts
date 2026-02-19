export interface Policy {
  id: string;
  organizationId: string;
  name: string;
  description: string | null;
  allowedCommandPatterns: string[];
  deniedCommandPatterns: string[];
  requireApproval: boolean;
  maxConcurrentCommands: number;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreatePolicyRequest {
  name: string;
  description?: string;
  allowedCommandPatterns: string[];
  deniedCommandPatterns: string[];
  requireApproval: boolean;
  maxConcurrentCommands: number;
}

export interface UpdatePolicyRequest extends Partial<CreatePolicyRequest> {
  isEnabled?: boolean;
}

export interface PolicyTestResult {
  isAllowed: boolean;
  denialReason: string | null;
  requiresApproval: boolean;
  matchedPattern: string | null;
}

export interface PolicyAssignment {
  id: string;
  userId: string;
  hostId: string | null;
  hostName: string | null;
  createdAt: string;
}

export interface PolicyPreset {
  name: string;
  description: string;
  allowedCommandPatterns: string[];
  deniedCommandPatterns: string[];
  requireApproval: boolean;
  maxConcurrentCommands: number;
}
