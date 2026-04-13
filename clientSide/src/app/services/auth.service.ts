import { Injectable } from '@angular/core';
import { LoginRequestDto } from '../models/auth';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environment/env';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = `${environment.baseUrl}/auth`;

  constructor(private http: HttpClient) { }

  login(payload: LoginRequestDto) {
    return this.http.post(`${this.apiUrl}/login`, payload, {
      withCredentials: true
    });
  }

  logout() {
    return this.http.post(`${this.apiUrl}/logout`, {}, {
      withCredentials: true
    });
  }
}
