// ── Auth ──────────────────────────────────────────────────────────────────────
export interface LoginRequest {
  deviceId: number;
  mACAddr:  string;
  iPAddr:   string;
}

export interface LoginResponse {
  success:    boolean;
  message:    string;
  deviceName: string;
  token:      string;
}

// ── Poll ──────────────────────────────────────────────────────────────────────
export interface TrnRow {
  trnID:    number;
  msgStr:   string;
  retryCnt: number;
}

export interface PollResponse {
  hasData:       boolean;
  batchToken:    string;
  needAckFirst:  boolean;
  rows:          TrnRow[];
  totalPending:  number;
}

// ── ACK ───────────────────────────────────────────────────────────────────────
export interface AckRequest {
  batchToken: string;
  trnIDs:     number[];
}

export interface AckResponse {
  success:      boolean;
  message:      string;
  updatedCount: number;
}
