"use client";

import { useState } from "react";
import { useCredentials } from "@/hooks/useCredentials";
import { CredentialType } from "@/types";
import type { CreateCredentialRequest } from "@/types";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import { Badge } from "@/components/ui/Badge";
import { Card } from "@/components/ui/Card";
import { Alert } from "@/components/ui/Alert";
import { Label } from "@/components/ui/Label";
import { PlusIcon, XIcon, TrashIcon } from "@/components/ui/Icons";

const typeLabels: Record<string, string> = {
  Password: "Password",
  SSHKey: "SSH Key",
  APIToken: "API Token",
};

export default function CredentialsPage() {
  const { credentials, loading, error, createCredential, deleteCredential } = useCredentials();
  const [showCreate, setShowCreate] = useState(false);
  const [form, setForm] = useState<CreateCredentialRequest>({
    name: "",
    credentialType: CredentialType.Password,
    value: "",
  });
  const [creating, setCreating] = useState(false);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreating(true);
    try {
      await createCredential(form);
      setShowCreate(false);
      setForm({ name: "", credentialType: CredentialType.Password, value: "" });
    } catch {
      // error handling via hook
    } finally {
      setCreating(false);
    }
  };

  const formatDate = (dateStr: string) =>
    new Date(dateStr).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full p-6">
        <p className="text-muted-foreground text-sm">Loading credentials...</p>
      </div>
    );
  }

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-lg font-semibold text-foreground">Credentials</h1>
          <p className="text-sm text-muted-foreground">
            {credentials.length} credential{credentials.length !== 1 ? "s" : ""} stored
          </p>
        </div>
        <Button
          onClick={() => setShowCreate(!showCreate)}
          variant={showCreate ? "secondary" : "primary"}
          size="sm"
        >
          {showCreate ? <><XIcon size={14} /> Cancel</> : <><PlusIcon size={14} /> Add Credential</>}
        </Button>
      </div>

      {error && <Alert className="mb-4">{error}</Alert>}

      {showCreate && (
        <Card className="mb-6 p-5">
          <h2 className="text-sm font-semibold text-foreground mb-4">New Credential</h2>
          <form onSubmit={handleCreate} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <Label htmlFor="cred-name">Name</Label>
                <Input
                  id="cred-name"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  required
                  placeholder="my-server-key"
                />
              </div>
              <div>
                <Label htmlFor="cred-type">Type</Label>
                <Select
                  id="cred-type"
                  value={form.credentialType}
                  onChange={(e) => setForm({ ...form, credentialType: e.target.value as CredentialType })}
                >
                  {Object.values(CredentialType).map((t) => (
                    <option key={t} value={t}>{typeLabels[t] || t}</option>
                  ))}
                </Select>
              </div>
              <div className="sm:col-span-2">
                <Label htmlFor="cred-value">
                  {form.credentialType === CredentialType.SSHKey ? "Private Key" : "Value"}
                </Label>
                {form.credentialType === CredentialType.SSHKey ? (
                  <Textarea
                    id="cred-value"
                    value={form.value}
                    onChange={(e) => setForm({ ...form, value: e.target.value })}
                    required
                    rows={6}
                    className="font-mono text-xs"
                    placeholder="-----BEGIN OPENSSH PRIVATE KEY-----"
                  />
                ) : (
                  <Input
                    id="cred-value"
                    type="password"
                    value={form.value}
                    onChange={(e) => setForm({ ...form, value: e.target.value })}
                    required
                    placeholder={form.credentialType === CredentialType.APIToken ? "token_..." : "Enter password"}
                  />
                )}
              </div>
            </div>
            <div className="flex justify-end">
              <Button type="submit" disabled={creating}>
                {creating ? "Creating..." : "Create Credential"}
              </Button>
            </div>
          </form>
        </Card>
      )}

      <div className="space-y-2">
        {credentials.map((cred) => (
          <Card key={cred.id} className="px-4 py-3">
            <div className="flex flex-col sm:flex-row sm:items-center gap-3">
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium text-foreground">{cred.name}</p>
                <p className="text-xs text-muted-foreground mt-0.5">Created {formatDate(cred.createdAt)}</p>
              </div>
              <div className="flex items-center gap-2">
                <Badge variant="neutral">{typeLabels[cred.credentialType] || cred.credentialType}</Badge>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => deleteCredential(cred.id)}
                  className="text-muted-foreground hover:text-destructive"
                >
                  <TrashIcon size={13} />
                </Button>
              </div>
            </div>
          </Card>
        ))}
        {credentials.length === 0 && (
          <div className="text-center py-16 text-muted-foreground">
            <p className="font-medium">No credentials stored yet</p>
            <p className="text-sm mt-1">Add a credential to link to your hosts</p>
          </div>
        )}
      </div>
    </div>
  );
}
