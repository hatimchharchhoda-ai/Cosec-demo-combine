import { Injectable } from '@angular/core';
import { ApiServiceService } from '../api/api-service.service';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class AuthServiceService {
  constructor(private api: ApiServiceService, private http: HttpClient) {}

  login(data: any) {
    return this.http.post(`${environment.apiUrl}/auth/login`, data, { withCredentials: true });
  }

  checkAuth() {
    return this.http.get(`${environment.apiUrl}/auth/check`, { withCredentials: true });
  }
}
