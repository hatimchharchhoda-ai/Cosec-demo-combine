import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiResponse, UserDto, UserListResponse } from '../Model/user.model';

@Injectable({ providedIn: 'root' })
export class UserService {
  // ← your actual API base URL
  private apiUrl = 'https://localhost:7192/api/User';

  constructor(private http: HttpClient) {}

  // interceptor adds withCredentials automatically on all these calls
  getUsers(page = 1, pageSize = 10): Observable<ApiResponse<UserListResponse>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
   
    return this.http.get<ApiResponse<UserListResponse>>(`${this.apiUrl}/list`, { params });
  }

  getUser(id: string): Observable<ApiResponse<UserDto>> {
    return this.http.get<ApiResponse<UserDto>>(`${this.apiUrl}/${id}`);
  }

  addUser(user: UserDto): Observable<ApiResponse<UserDto>> {
    return this.http.post<ApiResponse<UserDto>>(`${this.apiUrl}/add`, user);
  }

  updateUser(user: UserDto): Observable<ApiResponse<object>> {
    return this.http.put<ApiResponse<object>>(`${this.apiUrl}/update`, user);
  }

  deleteUser(id: string): Observable<ApiResponse<object>> {
    return this.http.delete<ApiResponse<object>>(`${this.apiUrl}/delete/${id}`);
  }

  enrollUser(id: string): Observable<ApiResponse<object>> {
    return this.http.post<ApiResponse<object>>(`${this.apiUrl}/enroll/${id}`, {});
  }
}