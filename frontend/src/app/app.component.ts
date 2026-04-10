import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthServiceService } from './services/auth/auth-service.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule],
  template: `
    <div class="shell">
      <nav class="topnav" >
   
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
    .nav-brand { display: flex; align-items: center; gap: 9px; color: #e2e8f0; font-size: 1.05rem; font-weight: 700; letter-spacing: 0.5px; white-space: nowrap; }
    .brand-hex { color: #38bdf8; font-size: 1.3rem; line-height: 1; }
    .brand-text strong { color: #38bdf8; }

    .nav-links { display: flex; gap: 4px; flex: 1; }
    .nav-links a { display: flex; align-items: center; gap: 7px; padding: 7px 14px; border-radius: 7px; color: #64748b; text-decoration: none; font-size: 0.875rem; font-weight: 500; transition: all 0.15s; border: 1px solid transparent; }
    .nav-links a:hover { color: #cbd5e1; background: #f9fafc; }
    .nav-links a.active { color: #38bdf8; background: rgba(244, 246, 247, 0.08); border-color: rgba(246, 250, 252, 0.2); }

    .nav-right { margin-left: auto; display: flex; align-items: center; gap: 12px; }
    .nav-user { font-size: 0.8rem; color: #475569; font-family: 'Courier New', monospace; font-weight: 600; background: #eceef1; padding: 4px 10px; border-radius: 5px; border: 1px solid #1e2a3a; }
    .btn-logout { display: flex; align-items: center; gap: 6px; padding: 7px 13px; background: transparent; border: 1px solid #f0f2f5; border-radius: 7px; color: #64748b; font-size: 0.82rem; cursor: pointer; transition: all 0.15s; }
    .btn-logout:hover { border-color: rgba(248,113,113,0.4); color: #f87171; background: rgba(245, 241, 241, 0.05); }

    main { min-height: calc(100vh - 58px); }
    main:not(.with-nav) { min-height: 100vh; }
  `]
})
export class AppComponent {
  constructor(public auth: AuthServiceService, private router: Router) { }
}