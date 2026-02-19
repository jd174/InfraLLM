"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";

export interface PromptSettings {
  systemPrompt: string | null;
  personalizationPrompt: string | null;
  defaultModel: string | null;
}

export interface UpdatePromptSettingsRequest {
  systemPrompt?: string | null;
  personalizationPrompt?: string | null;
  defaultModel?: string | null;
}

export function usePromptSettings() {
  const [settings, setSettings] = useState<PromptSettings>({
    systemPrompt: null,
    personalizationPrompt: null,
    defaultModel: null,
  });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchSettings = useCallback(async () => {
    try {
      setLoading(true);
      const data = await api.get<PromptSettings>("/api/promptsettings");
      setSettings(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch prompt settings");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchSettings();
  }, [fetchSettings]);

  const updateSettings = async (data: UpdatePromptSettingsRequest) => {
    try {
      setSaving(true);
      const updated = await api.put<PromptSettings>("/api/promptsettings", data);
      setSettings(updated);
      return updated;
    } finally {
      setSaving(false);
    }
  };

  return { settings, loading, saving, error, fetchSettings, updateSettings };
}
