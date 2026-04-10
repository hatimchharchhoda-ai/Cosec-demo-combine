import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UserService } from '../../services/user.service';
import { UserDto, ApiResponse, UserListResponse } from '../../Model/user.model';

@Component({
  selector: 'app-user-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page">
      <!-- Toast -->
      <div class="toast" [class.visible]="toast.show" [class.ok]="toast.ok">
        <svg *ngIf="toast.ok" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"/></svg>
        <svg *ngIf="!toast.ok" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
        {{ toast.msg }}
      </div>

      <div class="page-head">
        <div>
          <h1>User Form</h1>
          <p>Manage individual user records</p>
        </div>
      </div>

      <div class="card">

        <!-- Lookup bar -->
        <div class="lookup-strip">
          <span class="strip-label">LOAD USER</span>
          <div class="lookup-row">
            <input class="ctrl" type="text" [(ngModel)]="lookupId" placeholder="User ID (e.g. USR0000001)"
                   (keyup.enter)="loadUser()" />
            <button class="ghost-btn" (click)="loadUser()" [disabled]="loading">
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 102.13-9.36L1 10"/></svg>
              Load
            </button>
            <button class="ghost-btn" (click)="resetForm()">
              <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
              Clear
            </button>
          </div>
        </div>

        <div class="sep"></div>

        <!-- Form fields -->
        <div class="field-grid">

          <div class="field-block">
            <label class="flbl">User ID <span class="req">*</span></label>
            <input class="ctrl" [(ngModel)]="user.userId" placeholder="USR0000001" />
          </div>

          <div class="field-block">
            <label class="flbl">User Name <span class="req">*</span></label>
            <input class="ctrl" [(ngModel)]="user.userName" placeholder="Full name" />
          </div>

          <div class="field-block">
            <label class="flbl">Short Name</label>
            <input class="ctrl" [(ngModel)]="user.userShortName" placeholder="Alias / abbreviation" />
          </div>

          <div class="field-block">
            <label class="flbl">User IDN</label>
            <input class="ctrl" type="number" [(ngModel)]="user.userIDN" placeholder="Identity number" />
          </div>

          <div class="field-block full-col">
            <label class="flbl">Status</label>
            <div class="toggle-row">
              <label class="toggle">
                <input type="checkbox" [(ngModel)]="user.isActive" />
                <span class="track">
                  <span class="thumb"></span>
                </span>
              </label>
              <span class="status-text" [class.on]="user.isActive">
                {{ user.isActive ? '● Active' : '○ Inactive' }}
              </span>
            </div>
          </div>
        </div>

        <!-- 4 Action Buttons -->
        <div class="action-bar">
          <button class="act-btn add"    (click)="addUser()"    [disabled]="loading">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Add User
          </button>

          <button class="act-btn update" (click)="updateUser()" [disabled]="loading || !user.userId">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
            Update
          </button>

          <button class="act-btn delete" (click)="confirmDelete()" [disabled]="loading || !user.userId">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a1 1 0 011-1h4a1 1 0 011 1v2"/></svg>
            Delete
          </button>

          <button class="act-btn enroll" (click)="confirmEnroll()" [disabled]="loading || !user.userId">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 11.08V12a10 10 0 11-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
            Enroll
          </button>
        </div>

      </div>

      <!-- Confirm dialog -->
      <div class="overlay" *ngIf="dialog.show" (click)="dialog.show=false">
        <div class="dialog" (click)="$event.stopPropagation()">
          <div class="dlg-icon" [class]="dialog.iconClass">{{ dialog.icon }}</div>
          <h3 class="dlg-title">{{ dialog.title }}</h3>
          <p class="dlg-msg">{{ dialog.msg }}</p>
          <div class="dlg-btns">
            <button class="ghost-btn" (click)="dialog.show=false">Cancel</button>
            <button class="act-btn" [class]="dialog.btnClass" (click)="dialog.onOk()">
              {{ dialog.btnLabel }}
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
  * { box-sizing: border-box; }

  .page {
    max-width: 820px;
    margin: 0 auto;
    padding: 2rem;
    background: #f5f6f8;
    min-height: 100vh;
  }

  /* Toast */
  .toast {
    position: fixed;
    top: 20px;
    right: 20px;
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 10px 16px;
    border-radius: 6px;
    font-size: 14px;
    background: #ffecec;
    color: #d32f2f;
    border: 1px solid #f5c2c2;
    opacity: 0;
    transform: translateY(-10px);
    transition: 0.2s;
  }
  .toast.visible { opacity: 1; transform: translateY(0); }
  .toast.ok {
    background: #e6f4ea;
    color: #2e7d32;
    border-color: #b7e1c1;
  }

  /* Header */
  .page-head h1 {
    font-size: 22px;
    margin-bottom: 5px;
    color: #333;
  }
  .page-head p {
    color: #666;
    font-size: 14px;
  }

  /* Card */
  .card {
    background: #ffffff;
    border: 1px solid #ddd;
    border-radius: 8px;
  }

  /* Lookup */
  .lookup-strip {
    padding: 15px;
    background: #f9fafb;
    border-bottom: 1px solid #ddd;
  }
  .lookup-row {
    display: flex;
    gap: 8px;
  }

  .sep {
    height: 1px;
    background: #ddd;
  }

  /* Fields */
  .field-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 15px;
    padding: 20px;
  }
  .full-col { grid-column: 1 / -1; }

  .flbl {
    font-size: 13px;
    color: #444;
  }

  .ctrl {
    padding: 8px;
    border: 1px solid #ccc;
    border-radius: 4px;
    width: 100%;
  }

  /* Toggle simple */
  .toggle-row {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .status-text {
    font-size: 14px;
    color: #555;
  }

  /* Buttons */
  .ghost-btn {
    padding: 6px 12px;
    border: 1px solid #ccc;
    background: #fff;
    cursor: pointer;
    border-radius: 4px;
  }
  .ghost-btn:hover {
    background: #eee;
  }

  .action-bar {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    border-top: 1px solid #ddd;
  }

  .act-btn {
    padding: 12px;
    border: none;
    cursor: pointer;
    font-size: 14px;
  }

  .add { background: #e8f5e9; }
  .update { background: #e3f2fd; }
  .delete { background: #ffebee; }
  .enroll { background: #fff8e1; }

  .act-btn:hover {
    opacity: 0.8;
  }

  /* Dialog */
  .overlay {
    position: fixed;
    inset: 0;
    background: rgba(0,0,0,0.3);
    display: flex;
    justify-content: center;
    align-items: center;
  }

  .dialog {
    background: white;
    padding: 20px;
    border-radius: 8px;
    width: 300px;
    text-align: center;
  }

  .dlg-title {
    font-size: 18px;
    margin-bottom: 10px;
  }

  .dlg-msg {
    font-size: 14px;
    margin-bottom: 15px;
  }

  .dlg-btns {
    display: flex;
    justify-content: space-between;
  }
`]
})
export class UserFormComponent {
  user: UserDto = this.blank();
  lookupId = '';
  loading  = false;

  toast   = { show: false, msg: '', ok: true };
  private toastTimer: any;

  dialog = {
    show: false, icon: '', iconClass: '',
    title: '', msg: '',
    btnLabel: '', btnClass: '',
    onOk: () => {}
  };

  constructor(private svc: UserService) {}

  blank(): UserDto {
    return { userId: '', userName: '', isActive: true, userShortName: '', userIDN: null };
  }

  showToast(msg: string, ok: boolean) {
    clearTimeout(this.toastTimer);
    this.toast = { show: true, msg, ok };
    this.toastTimer = setTimeout(() => this.toast.show = false, 3500);
  }

  resetForm() { this.user = this.blank(); this.lookupId = ''; }

  loadUser() {
    const id = this.lookupId.trim();
    if (!id) { this.showToast('Enter a User ID to load.', false); return; }
    this.loading = true;
    this.svc.getUser(id).subscribe({
      next: r => {
        if (r.success && r.data) {
          this.user    = r.data;
          this.lookupId = r.data.userId;
          this.showToast(`Loaded: ${r.data.userName}`, true);
        } else {
          this.showToast(r.message || 'User not found.', false);
        }
        this.loading = false;
      },
      error: () => { this.showToast('Failed to load user.', false); this.loading = false; }
    });
  }

  addUser() {
    if (!this.user.userId?.trim())   { this.showToast('User ID is required.', false);   return; }
    if (!this.user.userName?.trim()) { this.showToast('User Name is required.', false); return; }
    this.loading = true;
    this.svc.addUser(this.user).subscribe({
      next: r => {
        this.showToast(r.success ? 'User added successfully!' : (r.message || 'Add failed.'), r.success);
        this.loading = false;
      },
      error: e => { this.showToast(e?.error?.message || 'Error adding user.', false); this.loading = false; }
    });
  }

  updateUser() {
    if (!this.user.userId?.trim()) { this.showToast('Load a user first.', false); return; }
    this.loading = true;
    this.svc.updateUser(this.user).subscribe({
      next: r => {
        this.showToast(r.success ? 'User updated!' : (r.message || 'Update failed.'), r.success);
        this.loading = false;
      },
      error: e => { this.showToast(e?.error?.message || 'Error updating user.', false); this.loading = false; }
    });
  }

  confirmDelete() {
    if (!this.user.userId?.trim()) { this.showToast('Load a user first.', false); return; }
    this.dialog = {
      show: true, icon: '🗑️', iconClass: 'del',
      title: 'Delete User',
      msg: `Delete "${this.user.userName}" (${this.user.userId})? This cannot be undone.`,
      btnLabel: 'Delete', btnClass: 'delete',
      onOk: () => { this.dialog.show = false; this.deleteUser(); }
    };
  }

  deleteUser() {
    this.loading = true;
    this.svc.deleteUser(this.user.userId).subscribe({
      next: r => {
        if (r.success) { this.resetForm(); this.showToast('User deleted.', true); }
        else { this.showToast(r.message || 'Delete failed.', false); }
        this.loading = false;
      },
      error: e => { this.showToast(e?.error?.message || 'Error deleting user.', false); this.loading = false; }
    });
  }

  confirmEnroll() {
    if (!this.user.userId?.trim()) { this.showToast('Load a user first.', false); return; }
    this.dialog = {
      show: true, icon: '✅', iconClass: 'enr',
      title: 'Enroll User',
      msg: `Enroll "${this.user.userName}" (${this.user.userId}) into Mat_CommTrn?`,
      btnLabel: 'Enroll', btnClass: 'enroll',
      onOk: () => { this.dialog.show = false; this.enrollUser(); }
    };
  }

  enrollUser() {
    this.loading = true;
    this.svc.enrollUser(this.user.userId).subscribe({
      next: r => {
        this.showToast(r.success ? (r.message || 'Enrolled!') : (r.message || 'Enroll failed.'), r.success);
        this.loading = false;
      },
      error: e => { this.showToast(e?.error?.message || 'Error enrolling user.', false); this.loading = false; }
    });
  }
}