import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { PollService, LogEntry } from '../../services/poll.service';
import { TrnRow } from '../../models/models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './dashboard.component.html'
})
export class DashboardComponent implements OnInit, OnDestroy {

  deviceName   = '';
  status       = '';
  isPolling    = false;
  totalPending = 0;
  rows: TrnRow[]      = [];
  logs: LogEntry[]    = [];

  private subs: Subscription[] = [];

  constructor(
    public auth: AuthService,
    public poll: PollService
  ) {}

  ngOnInit(): void {
    this.deviceName = this.auth.getDeviceName();

    // Subscribe to all observables from PollService
    this.subs.push(
      this.poll.status$.subscribe(s       => this.status       = s),
      this.poll.isPolling$.subscribe(v    => this.isPolling    = v),
      this.poll.totalPending$.subscribe(n => this.totalPending = n),
      this.poll.rows$.subscribe(r         => this.rows         = r),
      this.poll.logs$.subscribe(l         => this.logs         = l)
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  startPolling():  void { this.poll.start(); }
  stopPolling():   void { this.poll.stop(); }
  clearLogs():     void { this.poll.clearLogs(); }
  logout():        void { this.poll.stop(); this.auth.logout(); }

  // Parse MsgStr JSON for display
  parseMsg(msgStr: string): string {
    try {
      const obj = JSON.parse(msgStr);
      return Object.entries(obj)
        .map(([k, v]) => `${k}: ${v}`)
        .join(' | ');
    } catch {
      return msgStr;
    }
  }

  // CSS class for log type
  logClass(type: string): string {
    const map: Record<string, string> = {
      info:    'log-info',
      success: 'log-success',
      warn:    'log-warn',
      error:   'log-error'
    };
    return map[type] ?? 'log-info';
  }
}
