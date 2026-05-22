import { Component, OnInit } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthServiceService } from './services/auth/auth-service.service';
import { ApiServiceService } from './services/api/api-service.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule, FormsModule],
  template: `
    <div class="shell">
      <!-- Only show main navigation and routes if database is configured -->
      <ng-container *ngIf="isDbConfigured">
        <nav class="topnav">
          <div class="nav-links">
            <a routerLink="/user-form" routerLinkActive="active">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 20h9"/><path d="M16.5 3.5a2.121 2.121 0 013 3L7 19l-4 1 1-4L16.5 3.5z"/></svg>
              User Form
            </a>
            <a routerLink="/user-list" routerLinkActive="active">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>
              User List
            </a>
            <a routerLink="/device" routerLinkActive="active">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>
              Device List
            </a>
          </div>
        </nav>
        <router-outlet />
      </ng-container>

      <!-- Premium Dark Glassmorphic DB Config Overlay -->
      <div class="config-overlay" *ngIf="!isDbConfigured">
        <div class="config-card">
          <div class="config-header">
            <div class="logo-accent">
              <span class="logo-symbol">❖</span>
            </div>
            <h2>Database Setup</h2>
            <p class="subtitle">Please configure your SQL Server connection details below.</p>
          </div>

          <!-- Step 1: SQL Server Connection Fields -->
          <div class="form-container" *ngIf="step === 1">
            <div class="input-group">
              <label for="server">SQL Server \ Instance</label>
              <input type="text" id="server" [(ngModel)]="server" placeholder="e.g. localhost\SQLEXPRESS or 192.168.1.100" autocomplete="off" />
            </div>

            <div class="input-group">
              <label for="database">Database Name</label>
              <input type="text" id="database" [(ngModel)]="database" placeholder="e.g. matgen" autocomplete="off" />
            </div>

            <div class="input-group">
              <label for="username">Database Username</label>
              <input type="text" id="username" [(ngModel)]="username" placeholder="e.g. sa" autocomplete="off" />
            </div>

            <div class="input-group">
              <label for="password">Database Password</label>
              <input type="password" id="password" [(ngModel)]="password" placeholder="••••••••" />
            </div>

            <div class="error-msg" *ngIf="errorMessage">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
              <span>{{ errorMessage }}</span>
            </div>

            <button class="btn-primary" (click)="testConnection()" [disabled]="isLoading">
              <span *ngIf="!isLoading">Verify & Continue</span>
              <span class="spinner" *ngIf="isLoading"></span>
              <span *ngIf="isLoading">{{ loadingMessage }}</span>
            </button>
          </div>

          <!-- Step 2: Administrator User Creation (For New DBs) -->
          <div class="form-container" *ngIf="step === 2">
            <div class="alert-info">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
              <div>
                <strong>New Database Detected!</strong>
                <p>Please register the initial administrator credentials for this application.</p>
              </div>
            </div>

            <div class="input-group">
              <label for="adminUsername">Admin Username</label>
              <input type="text" id="adminUsername" [(ngModel)]="adminUsername" placeholder="e.g. admin" autocomplete="off" />
            </div>

            <div class="input-group">
              <label for="adminPassword">Admin Password</label>
              <input type="password" id="adminPassword" [(ngModel)]="adminPassword" placeholder="••••••••" />
            </div>

            <div class="error-msg" *ngIf="errorMessage">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
              <span>{{ errorMessage }}</span>
            </div>

            <div class="btn-row">
              <button class="btn-secondary" (click)="goBack()" [disabled]="isLoading">Back</button>
              <button class="btn-primary" (click)="saveConnection()" [disabled]="isLoading">
                <span *ngIf="!isLoading">Create DB & Save</span>
                <span class="spinner" *ngIf="isLoading"></span>
                <span *ngIf="isLoading">{{ loadingMessage }}</span>
              </button>
            </div>
          </div>

          <!-- Step 3: Success Screen / Polling Loop -->
          <div class="form-container text-center" *ngIf="step === 3">
            <div class="success-icon animate-pulse">
              <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#10b981" stroke-width="2.5"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
            </div>
            <h3>{{ successMessage }}</h3>
            <div class="progress-bar">
              <div class="progress-bar-value"></div>
            </div>
            <p class="loading-subtext">{{ loadingMessage }}</p>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    * { margin: 0; padding: 0; box-sizing: border-box; }
    :host { display: block; }
    .shell { min-height: 100vh; background: #edeef0; font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; }

    .topnav {
      display: flex; align-items: center; height: 58px; padding: 0 1.75rem;
      background: #f2f2f5; border-bottom: 1px solid #161d2a;
      position: sticky; top: 0; z-index: 200; gap: 1.5rem;
    }
    .nav-links { display: flex; gap: 4px; flex: 1; }
    .nav-links a { display: flex; align-items: center; gap: 7px; padding: 7px 14px; border-radius: 7px; color: #64748b; text-decoration: none; font-size: 0.875rem; font-weight: 500; transition: all 0.15s; border: 1px solid transparent; }
    .nav-links a:hover { color: #1e293b; background: #f9fafc; }
    .nav-links a.active { color: #38bdf8; background: rgba(30, 41, 59, 0.05); border-color: rgba(30, 41, 59, 0.1); }

    /* ── STUNNING GLASSMORPHIC OVERLAY ── */
    .config-overlay {
      position: fixed; top: 0; left: 0; right: 0; bottom: 0;
      background: radial-gradient(circle at center, #1e293b, #0f172a);
      display: flex; align-items: center; justify-content: center;
      z-index: 9999; padding: 1.5rem;
    }

    .config-card {
      width: 100%; max-width: 480px;
      background: rgba(30, 41, 59, 0.7);
      backdrop-filter: blur(20px);
      -webkit-backdrop-filter: blur(20px);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 20px;
      padding: 2.5rem 2rem;
      box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
      animation: fadeIn 0.4s cubic-bezier(0.16, 1, 0.3, 1);
    }

    .config-header {
      text-align: center; margin-bottom: 2rem;
    }

    .logo-accent {
      width: 52px; height: 52px;
      background: linear-gradient(135deg, #38bdf8, #0284c7);
      border-radius: 14px;
      display: flex; align-items: center; justify-content: center;
      margin: 0 auto 1.25rem;
      box-shadow: 0 10px 20px rgba(56, 189, 248, 0.25);
    }
    
    .logo-symbol {
      color: #ffffff; font-size: 1.6rem; font-weight: bold;
    }

    .config-header h2 {
      color: #f8fafc; font-size: 1.75rem; font-weight: 700; letter-spacing: -0.5px; margin-bottom: 0.5rem;
    }

    .subtitle {
      color: #94a3b8; font-size: 0.9rem; line-height: 1.4;
    }

    .form-container {
      display: flex; flex-direction: column; gap: 1.25rem;
    }

    .input-group {
      display: flex; flex-direction: column; gap: 0.5rem;
    }

    .input-group label {
      color: #cbd5e1; font-size: 0.825rem; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;
    }

    .input-group input {
      background: rgba(15, 23, 42, 0.4);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      padding: 0.75rem 1rem;
      color: #f8fafc;
      font-size: 0.95rem;
      transition: all 0.2s ease;
    }

    .input-group input::placeholder {
      color: #475569;
    }

    .input-group input:focus {
      outline: none; border-color: #38bdf8;
      box-shadow: 0 0 0 3px rgba(56, 189, 248, 0.15);
      background: rgba(15, 23, 42, 0.6);
    }

    /* ── ALERT BOX ── */
    .alert-info {
      background: rgba(56, 189, 248, 0.1);
      border-left: 4px solid #38bdf8;
      border-radius: 8px;
      padding: 1rem;
      display: flex; gap: 0.75rem; color: #bae6fd; font-size: 0.85rem; line-height: 1.4;
    }
    .alert-info strong {
      display: block; font-weight: 600; margin-bottom: 0.25rem; color: #f8fafc;
    }

    /* ── ERROR MESSAGE ── */
    .error-msg {
      background: rgba(239, 68, 68, 0.1);
      border: 1px solid rgba(239, 68, 68, 0.2);
      color: #fca5a5;
      padding: 0.75rem 1rem;
      border-radius: 10px;
      font-size: 0.85rem;
      display: flex; align-items: center; gap: 0.5rem;
      line-height: 1.4;
      animation: shake 0.4s ease;
    }

    /* ── BUTTONS ── */
    .btn-primary {
      background: linear-gradient(135deg, #38bdf8, #0284c7);
      color: #ffffff;
      border: none;
      border-radius: 10px;
      padding: 0.85rem;
      font-size: 0.95rem;
      font-weight: 600;
      cursor: pointer;
      display: flex; align-items: center; justify-content: center; gap: 0.5rem;
      transition: all 0.2s ease;
      box-shadow: 0 4px 12px rgba(56, 189, 248, 0.15);
    }

    .btn-primary:hover:not(:disabled) {
      background: linear-gradient(135deg, #0ea5e9, #0369a1);
      transform: translateY(-1px);
      box-shadow: 0 6px 16px rgba(56, 189, 248, 0.25);
    }

    .btn-primary:disabled {
      opacity: 0.6; cursor: not-allowed;
    }

    .btn-row {
      display: flex; gap: 1rem;
    }

    .btn-secondary {
      flex: 1;
      background: rgba(15, 23, 42, 0.4);
      color: #94a3b8;
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      padding: 0.85rem;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s ease;
    }

    .btn-secondary:hover:not(:disabled) {
      background: rgba(15, 23, 42, 0.6);
      color: #cbd5e1;
    }

    .btn-primary {
      flex: 2;
    }

    .text-center {
      text-align: center;
    }

    .success-icon {
      margin: 1.5rem auto;
      display: flex; align-items: center; justify-content: center;
    }

    .config-card h3 {
      color: #f8fafc; font-size: 1.3rem; font-weight: 600; margin-bottom: 1.25rem;
    }

    .loading-subtext {
      color: #94a3b8; font-size: 0.85rem;
    }

    /* ── PROGRESS BAR ANIMATION ── */
    .progress-bar {
      height: 6px; width: 100%;
      background-color: rgba(255, 255, 255, 0.08);
      border-radius: 3px;
      overflow: hidden;
      margin: 1.5rem 0;
    }

    .progress-bar-value {
      width: 100%; height: 100%;
      background-color: #38bdf8;
      animation: indeterminate 2s infinite linear;
      transform-origin: 0% 50%;
    }

    /* ── SPINNER ── */
    .spinner {
      width: 16px; height: 16px;
      border: 2px solid rgba(255,255,255,0.3);
      border-radius: 50%;
      border-top-color: #fff;
      animation: spin 0.8s infinite linear;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    @keyframes indeterminate {
      0% { transform:  translateX(0) scaleX(0); }
      40% { transform:  translateX(0) scaleX(0.4); }
      100% { transform:  translateX(100%) scaleX(0.5); }
    }

    @keyframes fadeIn {
      from { opacity: 0; transform: scale(0.96); }
      to { opacity: 1; transform: scale(1); }
    }

    @keyframes shake {
      0%, 100% { transform: translateX(0); }
      20%, 60% { transform: translateX(-4px); }
      40%, 80% { transform: translateX(4px); }
    }

    .animate-pulse {
      animation: pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
    }

    @keyframes pulse {
      0%, 100% { opacity: 1; transform: scale(1); }
      50% { opacity: .7; transform: scale(0.96); }
    }
  `]
})
export class AppComponent implements OnInit {
  isDbConfigured = true; // Default to true to prevent screen flash
  step = 1;
  isLoading = false;
  loadingMessage = '';
  errorMessage = '';
  successMessage = '';

  // SQL connection inputs
  server = '';
  database = '';
  username = '';
  password = '';

  // Flag indicating if the database already exists (from test connection endpoint)
  dbExists = true;

  // New admin user registration details
  adminUsername = '';
  adminPassword = '';

  constructor(
    public auth: AuthServiceService, 
    private router: Router,
    private api: ApiServiceService
  ) { }

  ngOnInit() {
    this.checkConfigStatus();
  }

  checkConfigStatus() {
    this.api.get('config/status').subscribe({
      next: (res: any) => {
        this.isDbConfigured = res && res.isConfigured;
      },
      error: () => {
        // If config endpoint fails on first startup, treat as unconfigured
        this.isDbConfigured = false;
      }
    });
  }

  testConnection() {
    if (!this.server || !this.database) {
      this.errorMessage = 'Server and Database name are required.';
      return;
    }

    this.errorMessage = '';
    this.isLoading = true;
    this.loadingMessage = 'Validating connection...';

    const payload = {
      server: this.server,
      database: this.database,
      username: this.username,
      password: this.password
    };

    this.api.post('config/test', payload).subscribe({
      next: (res: any) => {
        this.isLoading = false;
        if (res.success) {
          this.dbExists = res.dbExists;
          if (this.dbExists) {
            // Database exists, save directly
            this.saveConnection();
          } else {
            // Database does not exist, move to Screen 2 to create database & admin user
            this.step = 2;
          }
        } else {
          this.errorMessage = res.message || 'Connection failed. Please check your credentials.';
        }
      },
      error: (err: any) => {
        this.isLoading = false;
        this.errorMessage = err.error?.message || 'Verification request failed. Check server status.';
      }
    });
  }

  saveConnection() {
    this.errorMessage = '';
    this.isLoading = true;
    this.loadingMessage = this.dbExists 
      ? 'Saving connection details...' 
      : 'Creating database & initializing tables...';

    const payload = {
      server: this.server,
      database: this.database,
      username: this.username,
      password: this.password,
      dbExists: this.dbExists,
      adminUsername: this.adminUsername,
      adminPassword: this.adminPassword
    };

    this.api.post('config/setup', payload).subscribe({
      next: (res: any) => {
        this.step = 3;
        this.isLoading = false;
        this.successMessage = 'Connection Configuration Saved!';
        this.pollConfigStatus();
      },
      error: (err: any) => {
        this.isLoading = false;
        this.errorMessage = err.error?.message || 'Failed to complete database configuration.';
      }
    });
  }

  goBack() {
    this.errorMessage = '';
    this.step = 1;
  }

  pollConfigStatus() {
    this.loadingMessage = 'Restarting backend service... Please stand by.';
    
    // Poll the status endpoint every 2 seconds to detect when service is back up
    const interval = setInterval(() => {
      this.api.get('config/status').subscribe({
        next: (res: any) => {
          if (res && res.isConfigured) {
            clearInterval(interval);
            this.loadingMessage = 'System is ready!';
            setTimeout(() => {
              window.location.reload();
            }, 1000);
          }
        },
        error: () => {
          // Ignore network errors while process is down / rebooting
        }
      });
    }, 2000);
  }
}