export type JobTriggerType = "Cron" | "Webhook";

export interface Job {
  id: string;
  organizationId: string;
  userId: string;
  name: string;
  description: string | null;
  prompt: string | null;
  triggerType: JobTriggerType;
  cronSchedule: string | null;
  webhookSecret: string | null;
  autoRunLlm: boolean;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  lastRunAt: string | null;
}

export interface JobRun {
  id: string;
  jobId: string;
  sessionId: string | null;
  triggeredBy: string;
  status: string;
  payload: string;
  response: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface CreateJobRequest {
  name: string;
  description?: string;
  prompt?: string;
  triggerType: JobTriggerType;
  cronSchedule?: string;
  webhookSecret?: string;
  autoRunLlm?: boolean;
  isEnabled?: boolean;
}

export interface UpdateJobRequest {
  name?: string;
  description?: string;
  prompt?: string;
  triggerType?: JobTriggerType;
  cronSchedule?: string;
  webhookSecret?: string;
  autoRunLlm?: boolean;
  isEnabled?: boolean;
}
