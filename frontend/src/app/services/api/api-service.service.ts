import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { HttpClient } from '@angular/common/http';

@Injectable({
  providedIn: 'root'
})
export class ApiServiceService {

  constructor(private http: HttpClient) { }

  get(url: string) {
    return this.http.get(`${environment.apiUrl}/${url}`);
  }

  post(url: string, body: any) {
    return this.http.post(`${environment.apiUrl}/${url}`, body);
  }

  put(url: string, body: any) {
    return this.http.put(`${environment.apiUrl}/${url}`, body);
  }

  delete(url: string) {
    return this.http.delete(`${environment.apiUrl}/${url}`);
  }
}
