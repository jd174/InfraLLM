export interface User {
  id: string;
  email: string;
  displayName: string;
  organizationId: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
  organizationName: string;
}

export interface AuthResponse {
  token: string;
  user: User;
}
