export enum CredentialType {
  Password = "Password",
  SSHKey = "SSHKey",
  APIToken = "APIToken",
}

export interface Credential {
  id: string;
  name: string;
  credentialType: CredentialType;
  createdAt: string;
  createdBy: string;
}

export interface CreateCredentialRequest {
  name: string;
  credentialType: CredentialType;
  value: string;
}
