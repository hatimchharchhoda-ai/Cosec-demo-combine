import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { LoginRequest, LoginResponse } from '../models/models';

@Injectable({ providedIn: 'root' })
export class AuthService {

  private token: string | null = null;

  constructor(private http: HttpClient, private router: Router) {
    // Restore token from localStorage on page reload
    this.token = localStorage.getItem('mat_token');
  }

  login(req: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('https://localhost:44327/api/auth/login', req, { withCredentials: true })
      .pipe(tap(res => {
        if (res.success) {
          this.token = res.token;
          localStorage.setItem('mat_token', res.token);
          localStorage.setItem('mat_device', res.deviceName);
        }
      }));
  }

  refresh(): Observable<any> {
    return this.http.post<any>('https://localhost:44327/api/auth/refresh', {}, { withCredentials: true })
      .pipe(tap(res => {
        if (res.success) {
          this.token = res.token;
          localStorage.setItem('mat_token', res.token);
        }
      }));
  }

  logout(): void {
    this.http.post('https://localhost:44327/api/auth/logout', {}, { withCredentials: true }).subscribe();
    this.token = null;
    localStorage.removeItem('mat_token');
    localStorage.removeItem('mat_device');
    this.router.navigate(['/login']);
  }

  getToken(): string | null  { return this.token; }
  isLoggedIn(): boolean       { return !!this.token; }
  getDeviceName(): string     { return localStorage.getItem('mat_device') ?? 'Device'; }
}
