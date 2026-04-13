import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { BehaviorSubject, interval, Subscription } from 'rxjs';
import { AuthService } from './auth.service';
import { AckRequest, AckResponse, PollResponse, TrnRow } from '../models/models';

export interface LogEntry {
  time:    string;
  type:    'info' | 'success' | 'warn' | 'error';
  message: string;
}

@Injectable({ providedIn: 'root' })
export class PollService {

  // Observables the UI subscribes to
  rows$         = new BehaviorSubject<TrnRow[]>([]);
  logs$         = new BehaviorSubject<LogEntry[]>([]);
  totalPending$ = new BehaviorSubject<number>(0);
  isPolling$    = new BehaviorSubject<boolean>(false);
  status$       = new BehaviorSubject<string>('Idle');

  private subscription?: Subscription;
  private pendingBatchToken: string | null = null;

  constructor(private http: HttpClient, private auth: AuthService) {}

  // ── Start polling every 8 seconds ─────────────────────────────────────────
  start(): void {
    if (this.subscription) return;
    this.isPolling$.next(true);
    this.status$.next('Polling every 8 seconds...');
    this.log('info', 'Polling started (every 8 seconds)');

    // Poll immediately, then every 8 seconds
    this.poll();
    this.subscription = interval(8000).subscribe(() => this.poll());
  }

  stop(): void {
    this.subscription?.unsubscribe();
    this.subscription = undefined;
    this.isPolling$.next(false);
    this.status$.next('Stopped');
    this.log('info', 'Polling stopped');
  }

  // ── Single poll cycle ─────────────────────────────────────────────────────
  private poll(): void {
    this.status$.next('Polling server...');

    this.http.get<PollResponse>('https://localhost:44327/api/poll', {
      headers: this.headers(),
      withCredentials: true
    }).subscribe({
      next: res => {
        this.totalPending$.next(res.totalPending ?? 0);

        // CASE: server says ACK first
        if (res.needAckFirst) {
          this.log('warn', `Server says ACK pending batch first (token: ${res.batchToken?.substring(0,8)}...)`);
          this.status$.next('Waiting for ACK...');
          if (res.batchToken) this.sendAck(res.batchToken, this.rows$.value.map(r => r.trnID));
          return;
        }

        // CASE: no data
        if (!res.hasData || res.rows?.length === 0) {
          this.log('info', `Poll: no pending data (${res.totalPending} total pending)`);
          this.status$.next(`No data — ${res.totalPending} pending in DB`);
          return;
        }

        // CASE: got data
        this.pendingBatchToken = res.batchToken;
        this.rows$.next(res.rows);
        this.log('success', `Received ${res.rows.length} rows — batch: ${res.batchToken?.substring(0,8)}...`);
        this.status$.next(`Processing ${res.rows.length} rows...`);

        // Auto-ACK after 1 second (simulates device processing)
        setTimeout(() => this.sendAck(res.batchToken, res.rows.map(r => r.trnID)), 1000);
      },
      error: err => {
        const msg = err.status === 401 ? 'Unauthorized — token expired' : `Poll error: ${err.message}`;
        this.log('error', msg);
        this.status$.next('Error — see log');
      }
    });
  }

  // ── Send ACK ──────────────────────────────────────────────────────────────
  sendAck(batchToken: string, trnIDs: number[]): void {
    if (!batchToken || trnIDs.length === 0) return;

    const body: AckRequest = { batchToken, trnIDs };
    this.log('info', `Sending ACK for ${trnIDs.length} rows...`);

    this.http.post<AckResponse>('https://localhost:44327/api/poll/ack', body, {
      headers: this.headers(),
      withCredentials: true
    }).subscribe({
      next: res => {
        if (res.success) {
          this.log('success', `ACK confirmed — ${res.updatedCount} rows marked done`);
          this.status$.next('ACK sent — ready for next batch');
          this.pendingBatchToken = null;
          this.rows$.next([]);
        } else {
          this.log('error', `ACK failed: ${res.message}`);
        }
      },
      error: err => this.log('error', `ACK error: ${err.message}`)
    });
  }

  // ── Helpers ───────────────────────────────────────────────────────────────
  private headers(): HttpHeaders {
    const token = this.auth.getToken();
    return token
      ? new HttpHeaders({ Authorization: `Bearer ${token}` })
      : new HttpHeaders();
  }

  private log(type: LogEntry['type'], message: string): void {
    const now     = new Date();
    const time    = now.toTimeString().substring(0, 8);
    const current = this.logs$.value;
    // Keep last 100 log entries
    const updated = [{ time, type, message }, ...current].slice(0, 100);
    this.logs$.next(updated);
  }

  clearLogs(): void { this.logs$.next([]); }
}
