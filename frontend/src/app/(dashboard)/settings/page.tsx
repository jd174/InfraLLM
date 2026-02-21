"use client";

import { useState } from "react";
import { useAuthStore } from "@/stores/authStore";
import { api } from "@/lib/api";
import { usePromptSettings, type PromptSettings } from "@/hooks/usePromptSettings";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Textarea } from "@/components/ui/Textarea";
import { Label } from "@/components/ui/Label";
import { Card, CardContent, CardHeader } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";

const APP_VERSION = "0.1.0";
const NEXT_VERSION = "16.1.6";
const STACK = "Next.js · .NET 9 · ASP.NET Core · EF Core · SignalR";

interface PasswordForm {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

type SaveStatus = "idle" | "saving" | "saved" | "error";

function useSaveStatus() {
  const [status, setStatus] = useState<SaveStatus>("idle");
  const [errorMsg, setErrorMsg] = useState("");

  const saving = () => setStatus("saving");
  const saved = () => {
    setStatus("saved");
    setTimeout(() => setStatus("idle"), 2500);
  };
  const error = (msg: string) => {
    setErrorMsg(msg);
    setStatus("error");
    setTimeout(() => setStatus("idle"), 4000);
  };
  const reset = () => setStatus("idle");

  return { status, errorMsg, saving, saved, error, reset };
}

export default function SettingsPage() {
  const { user } = useAuthStore();

  const [profileDisplayName, setProfileDisplayName] = useState<string | null>(null);
  const profileStatus = useSaveStatus();

  const [password, setPassword] = useState<PasswordForm>({
    currentPassword: "",
    newPassword: "",
    confirmPassword: "",
  });
  const passwordStatus = useSaveStatus();

  const { settings: promptSettings, updateSettings: updatePromptSettings } = usePromptSettings();
  const [promptDraft, setPromptDraft] = useState<PromptSettings>({
    systemPrompt: null,
    personalizationPrompt: null,
    defaultModel: null,
  });
  const promptStatus = useSaveStatus();

  const effectiveDisplayName = profileDisplayName ?? user?.displayName ?? "";
  const effectivePromptSettings = {
    systemPrompt: promptDraft.systemPrompt ?? promptSettings.systemPrompt ?? "",
    personalizationPrompt:
      promptDraft.personalizationPrompt ?? promptSettings.personalizationPrompt ?? "",
    defaultModel: promptDraft.defaultModel ?? promptSettings.defaultModel ?? "",
  };

  const handleSaveProfile = async (e: React.FormEvent) => {
    e.preventDefault();
    profileStatus.saving();
    try {
      await api.put("/api/auth/profile", { displayName: effectiveDisplayName });
      useAuthStore.setState((s) => ({
        user: s.user ? { ...s.user, displayName: effectiveDisplayName } : s.user,
      }));
      setProfileDisplayName(null);
      profileStatus.saved();
    } catch {
      profileStatus.error("Failed to update profile. Please try again.");
    }
  };

  const handleSavePassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (password.newPassword !== password.confirmPassword) {
      passwordStatus.error("New passwords do not match.");
      return;
    }
    if (password.newPassword.length < 8) {
      passwordStatus.error("New password must be at least 8 characters.");
      return;
    }
    passwordStatus.saving();
    try {
      await api.put("/api/auth/password", {
        currentPassword: password.currentPassword,
        newPassword: password.newPassword,
      });
      setPassword({ currentPassword: "", newPassword: "", confirmPassword: "" });
      passwordStatus.saved();
    } catch {
      passwordStatus.error("Failed to change password. Check your current password and try again.");
    }
  };

  const handleSavePrompt = async (e: React.FormEvent) => {
    e.preventDefault();
    promptStatus.saving();
    try {
      await updatePromptSettings({
        systemPrompt: effectivePromptSettings.systemPrompt || null,
        personalizationPrompt: effectivePromptSettings.personalizationPrompt || null,
        defaultModel: effectivePromptSettings.defaultModel || null,
      });
      setPromptDraft({ systemPrompt: null, personalizationPrompt: null, defaultModel: null });
      promptStatus.saved();
    } catch {
      promptStatus.error("Failed to save prompt settings.");
    }
  };

  return (
    <div className="p-4 md:p-6 max-w-3xl mx-auto space-y-8">
      <div>
        <h1 className="text-lg font-semibold text-foreground">Settings</h1>
        <p className="text-sm text-muted-foreground">Manage your account and preferences</p>
      </div>

      <Card>
        <CardHeader>
          <h2 className="text-sm font-semibold text-foreground">Profile</h2>
          <p className="text-xs text-muted-foreground mt-0.5">Update your display name</p>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSaveProfile} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="displayName">Display name</Label>
                <Input
                  id="displayName"
                  value={effectiveDisplayName}
                  onChange={(e) => setProfileDisplayName(e.target.value)}
                  placeholder="Your name"
                  autoComplete="name"
                />
              </div>
              <div className="space-y-1.5">
                <Label>Email</Label>
                <Input value={user?.email ?? ""} readOnly className="opacity-60 cursor-not-allowed" />
              </div>
            </div>

            {profileStatus.status === "error" && (
              <Alert variant="error">{profileStatus.errorMsg}</Alert>
            )}
            {profileStatus.status === "saved" && (
              <Alert variant="success">Profile updated successfully.</Alert>
            )}

            <div className="flex justify-end">
              <Button
                type="submit"
                variant="primary"
                size="sm"
                disabled={profileStatus.status === "saving"}
              >
                {profileStatus.status === "saving" ? "Saving…" : "Save profile"}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <h2 className="text-sm font-semibold text-foreground">Change password</h2>
          <p className="text-xs text-muted-foreground mt-0.5">Choose a new password for your account</p>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSavePassword} className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="currentPassword">Current password</Label>
              <Input
                id="currentPassword"
                type="password"
                value={password.currentPassword}
                onChange={(e) => setPassword((p) => ({ ...p, currentPassword: e.target.value }))}
                autoComplete="current-password"
              />
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="newPassword">New password</Label>
                <Input
                  id="newPassword"
                  type="password"
                  value={password.newPassword}
                  onChange={(e) => setPassword((p) => ({ ...p, newPassword: e.target.value }))}
                  autoComplete="new-password"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="confirmPassword">Confirm new password</Label>
                <Input
                  id="confirmPassword"
                  type="password"
                  value={password.confirmPassword}
                  onChange={(e) => setPassword((p) => ({ ...p, confirmPassword: e.target.value }))}
                  autoComplete="new-password"
                />
              </div>
            </div>

            {passwordStatus.status === "error" && (
              <Alert variant="error">{passwordStatus.errorMsg}</Alert>
            )}
            {passwordStatus.status === "saved" && (
              <Alert variant="success">Password changed successfully.</Alert>
            )}

            <div className="flex justify-end">
              <Button
                type="submit"
                variant="primary"
                size="sm"
                disabled={
                  passwordStatus.status === "saving" ||
                  !password.currentPassword ||
                  !password.newPassword ||
                  !password.confirmPassword
                }
              >
                {passwordStatus.status === "saving" ? "Saving…" : "Change password"}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <h2 className="text-sm font-semibold text-foreground">AI &amp; Prompt settings</h2>
          <p className="text-xs text-muted-foreground mt-0.5">Customize how the AI behaves for you</p>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSavePrompt} className="space-y-4">
            <div className="space-y-1.5">
              <Label htmlFor="defaultModel">Default model</Label>
              <Input
                id="defaultModel"
                value={effectivePromptSettings.defaultModel}
                onChange={(e) =>
                  setPromptDraft((s) => ({ ...s, defaultModel: e.target.value }))
                }
                placeholder="Auto (server default), gpt-5, gpt-4.1, llama3.1, ..."
              />
              <p className="text-xs text-muted-foreground">
                Leave blank to use the server default model for the active provider.
              </p>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="systemPrompt">System prompt</Label>
              <p className="text-xs text-muted-foreground">
                Instructions prepended to every conversation.
              </p>
              <Textarea
                id="systemPrompt"
                value={effectivePromptSettings.systemPrompt}
                onChange={(e) =>
                  setPromptDraft((s) => ({ ...s, systemPrompt: e.target.value }))
                }
                rows={5}
                placeholder="You are a helpful infrastructure assistant…"
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="personalizationPrompt">Personalization prompt</Label>
              <p className="text-xs text-muted-foreground">
                Personal context about you — your role, preferences, or working style.
              </p>
              <Textarea
                id="personalizationPrompt"
                value={effectivePromptSettings.personalizationPrompt}
                onChange={(e) =>
                  setPromptDraft((s) => ({ ...s, personalizationPrompt: e.target.value }))
                }
                rows={3}
                placeholder="I'm a senior SRE at Acme Corp. I prefer concise, actionable responses…"
              />
            </div>

            {promptStatus.status === "error" && (
              <Alert variant="error">{promptStatus.errorMsg}</Alert>
            )}
            {promptStatus.status === "saved" && (
              <Alert variant="success">Prompt settings saved.</Alert>
            )}

            <div className="flex justify-end">
              <Button
                type="submit"
                variant="primary"
                size="sm"
                disabled={promptStatus.status === "saving"}
              >
                {promptStatus.status === "saving" ? "Saving…" : "Save settings"}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <h2 className="text-sm font-semibold text-foreground">About</h2>
        </CardHeader>
        <CardContent>
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-4 text-sm">
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">Application</dt>
              <dd className="font-medium text-foreground">InfraLLM</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">Version</dt>
              <dd className="font-mono text-foreground">{APP_VERSION}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">Frontend</dt>
              <dd className="text-foreground">Next.js {NEXT_VERSION} (App Router)</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">Backend</dt>
              <dd className="text-foreground">.NET 9 · ASP.NET Core</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">Stack</dt>
              <dd className="text-foreground text-xs">{STACK}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">Signed in as</dt>
              <dd className="text-foreground truncate">{user?.email}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">Organization ID</dt>
              <dd className="font-mono text-xs text-foreground truncate">{user?.organizationId}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground mb-0.5">User ID</dt>
              <dd className="font-mono text-xs text-foreground truncate">{user?.id}</dd>
            </div>
          </dl>
        </CardContent>
      </Card>
    </div>
  );
}
