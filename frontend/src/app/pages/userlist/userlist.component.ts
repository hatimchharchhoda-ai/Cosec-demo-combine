import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, takeUntil } from 'rxjs';
import { UserService } from '../../services/user.service';
import { UserDto, ApiResponse, UserListResponse } from '../../Model/user.model';
@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="page">

      <div class="page-head">
        <div>
          <h1>User List</h1>
          <p *ngIf="!loading">
            {{ totalCount | number }} users &nbsp;·&nbsp;
            Page {{ currentPage }} of {{ totalPages }}
          </p>
          <p *ngIf="loading">Loading…</p>
        </div>
        <a routerLink="/user-form" class="add-link">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          Add User
        </a>
      </div>

      
      <!-- Table card -->
      <div class="tbl-card">

        <!-- Header -->
        <div class="tbl-head">
          <div class="tc id">User ID</div>
          <div class="tc name">User Name</div>
          <div class="tc short">Short Name</div>
          <div class="tc idn">IDN</div>
          <div class="tc status">Status</div>
        </div>

        <!-- Skeleton rows while loading -->
        <ng-container *ngIf="loading">
          <div class="skel-row" *ngFor="let i of skeletons">
            <div class="skel-cell w80"></div>
            <div class="skel-cell w200"></div>
            <div class="skel-cell w120"></div>
            <div class="skel-cell w140"></div>
            <div class="skel-cell w80"></div>
          </div>
        </ng-container>

        <!-- Data rows -->
        <ng-container *ngIf="!loading">
         

          <div class="tbl-row"
               *ngFor="let u of users; let i = index"
               [style.animation-delay.ms]="i * 30">
            <div class="tc id">
              <span class="id-tag">{{ u.userId }}</span>
            </div>
            <div class="tc name">
              <span class="avatar">{{ initial(u.userName) }}</span>
              <span class="uname">{{ u.userName || '—' }}</span>
            </div>
            <div class="tc short mono">{{ u.userShortName || '—' }}</div>
            <div class="tc idn  mono">{{ u.userIDN ?? '—' }}</div>
            <div class="tc status">
              <span class="badge" [class.active]="u.isActive" [class.inactive]="!u.isActive">
                <span class="dot"></span>
                {{ u.isActive ? 'Active' : 'Inactive' }}
              </span>
            </div>
          </div>
        </ng-container>
      </div>

      <!-- Pagination -->
      
      <div class="pager" *ngIf="totalPages > 1 && !loading">

        <button class="pg-btn" (click)="go(1)"               [disabled]="currentPage===1">«</button>
        <button class="pg-btn" (click)="go(currentPage - 1)" [disabled]="currentPage===1">‹</button>

        <ng-container *ngFor="let p of pageRange">
          <span class="pg-dots" *ngIf="p===-1">…</span>
          <button class="pg-btn" *ngIf="p!==-1"
                  [class.cur]="p===currentPage"
                  (click)="go(p)">{{ p }}</button>
        </ng-container>

        <button class="pg-btn" (click)="go(currentPage + 1)" [disabled]="currentPage===totalPages">›</button>
        <button class="pg-btn" (click)="go(totalPages)"      [disabled]="currentPage===totalPages">»</button>

        <span class="pg-info">
          {{ rangeStart }}–{{ rangeEnd }} of {{ totalCount | number }}
        </span>
      </div>
    </div>
  `,
  styles: [`
  * { box-sizing: border-box; }

  .page {
    max-width: 1000px;
    margin: 0 auto;
    padding: 2rem;
    background: #f5f6f8;
    min-height: 100vh;
  }

  .page-head {
    display: flex;
    justify-content: space-between;
    margin-bottom: 1.5rem;
  }

  .page-head h1 {
    font-size: 22px;
    color: #333;
  }

  .page-head p {
    font-size: 13px;
    color: #666;
  }

  .add-link {
    padding: 8px 14px;
    background: #e8f5e9;
    border: 1px solid #a5d6a7;
    color: #2e7d32;
    border-radius: 5px;
    text-decoration: none;
    font-size: 13px;
  }

  /* Search */
  .search-bar {
    display: flex;
    gap: 10px;
    margin-bottom: 15px;
  }

  .search-wrap {
    display: flex;
    align-items: center;
    flex: 1;
    background: #fff;
    border: 1px solid #ccc;
    border-radius: 5px;
    padding: 0 10px;
  }

  .search-wrap input {
    flex: 1;
    border: none;
    outline: none;
    padding: 8px;
  }

  .clear-search {
    border: none;
    background: none;
    cursor: pointer;
  }

  .result-count {
    font-size: 13px;
    color: #333;
  }

  /* Table */
  .tbl-card {
    background: #fff;
    border: 1px solid #ddd;
    border-radius: 6px;
  }

  .tbl-head, .tbl-row {
    display: grid;
    grid-template-columns: 130px 1fr 150px 170px 110px;
    padding: 10px 15px;
    border-bottom: 1px solid #eee;
  }

  .tbl-head {
    background: #f0f0f0;
    font-weight: bold;
    font-size: 12px;
  }

  .tbl-row:hover {
    background: #f9f9f9;
  }

  .id-tag {
    font-family: monospace;
    font-size: 12px;
    background: #e3f2fd;
    padding: 2px 6px;
    border-radius: 4px;
  }

  .tc.name {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .avatar {
    width: 25px;
    height: 25px;
    background: #ddd;
    border-radius: 4px;
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 12px;
  }

  .mono {
    font-family: monospace;
    font-size: 12px;
  }

  .badge {
    padding: 3px 8px;
    border-radius: 10px;
    font-size: 12px;
  }

  .badge.active {
    background: #e8f5e9;
    color: #2e7d32;
  }

  .badge.inactive {
    background: #eee;
    color: #666;
  }

  /* Empty */
  .empty-state {
    padding: 40px;
    text-align: center;
    color: #666;
  }

  /* Pagination */
  .pager {
    display: flex;
    justify-content: center;
    margin-top: 15px;
    gap: 5px;
  }

  .pg-btn {
    width: 30px;
    height: 30px;
    border: 1px solid #ccc;
    background: #fff;
    cursor: pointer;
  }

  .pg-btn.cur {
    background: #007bff;
    color: white;
  }

  .pg-btn:disabled {
    opacity: 0.5;
  }

  .pg-info {
    margin-left: 10px;
    font-size: 12px;
    color: #555;
  }

  /* Skeleton */
  .skel-row {
    display: flex;
    gap: 10px;
    padding: 10px;
  }

  .skel-cell {
    height: 10px;
    background: #eee;
    border-radius: 3px;
  }

  .w80 { width: 80px; }
  .w120 { width: 120px; }
  .w140 { width: 140px; }
  .w200 { width: 200px; }
`]
})
export class UserListComponent implements OnInit, OnDestroy {
  users       : UserDto[] = [];
  currentPage  = 1;
  totalPages   = 1;
  totalCount   = 0;
  pageSize     = 10;
  loading      = true;
  searchTerm   = '';

  skeletons    = Array(10);

  private search$ = new Subject<string>();
  private destroy$ = new Subject<void>();

  get rangeStart() { return (this.currentPage - 1) * this.pageSize + 1; }
  get rangeEnd()   { return Math.min(this.currentPage * this.pageSize, this.totalCount); }

  get pageRange(): number[] {
    if (this.totalPages <= 1) return [1];
    const cur = this.currentPage, total = this.totalPages;
    const pages: number[] = [1];
    if (cur - 2 > 2)  pages.push(-1);
    for (let p = Math.max(2, cur - 2); p <= Math.min(total - 1, cur + 2); p++) pages.push(p);
    if (cur + 2 < total - 1) pages.push(-1);
    if (total > 1) pages.push(total);
    return pages;
  }

  constructor(private svc: UserService) {}

  ngOnInit() {
    this.search$.pipe(
      debounceTime(350),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    ).subscribe(() => { this.currentPage = 1; this.load(); });

    this.load();
  }

  ngOnDestroy() { this.destroy$.next(); this.destroy$.complete(); }

  onSearch(val: string) { this.searchTerm = val; this.search$.next(val); }

  clearSearch() { this.searchTerm = ''; this.search$.next(''); }

  go(page: number) {
    if (page < 1 || page > this.totalPages || page === this.currentPage) return;
    this.currentPage = page;
    this.load();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  load() {
    this.loading = true;
    this.svc.getUsers(this.currentPage, this.pageSize).subscribe({
      next: r => {
        if (r.success && r.data) {
          this.users      = r.data.users;
          this.totalCount = r.data.totalCount;
          this.totalPages = r.data.totalPages;
        }
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  initial(name: string | null): string {
    return name ? name.charAt(0).toUpperCase() : '?';
  }
}