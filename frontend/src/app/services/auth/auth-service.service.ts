import { Injectable, signal } from '@angular/core';
import { ApiServiceService } from '../api/api-service.service';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, catchError, of, map } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})


export class AuthServiceService {

  readonly userId = signal<string | null>(null);

  constructor(private api: ApiServiceService, private http: HttpClient) {}

  login(data: any) {
    return this.http.post(`${environment.apiUrl}/auth/login`, data, { withCredentials: true }).pipe(
      tap((response: any) => {
        this.userId.set(response.userId);
      })
    );
  }

  checkAuth() {
    return this.http.get(`${environment.apiUrl}/auth/check`, { withCredentials: true });
  }

 logout(): Observable<unknown> {
    return this.http.post(`${environment.apiUrl}/logout`, {}).pipe(
      tap(() => this.userId.set(null)),
      catchError(() => {
        this.userId.set(null);  // always clear signal even if API fails
        return of(null);
      })
    );
  }

  isLoggedIn(): boolean  { return this.userId() !== null; }
  getCurrentUser(): string | null { return this.userId(); }

}
