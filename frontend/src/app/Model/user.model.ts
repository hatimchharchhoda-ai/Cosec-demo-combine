// ── Auth ──────────────────────────────────────────────────────
export interface LoginRequest {
  loginUserId: string;
  loginPassword: string;
}

export interface LoginResponse {
  token: string;
  userId: string;
  message: string;
}

// ── User ──────────────────────────────────────────────────────
export interface UserDto {
  userId: string;
  userName: string | null;
  isActive: boolean;
  userShortName: string | null;
  userIDN: number | null;
}

export interface UserListResponse {
  users: UserDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface ApiResponse<T> {
  success: boolean;
  message: string | null;
  data: T | null;
}