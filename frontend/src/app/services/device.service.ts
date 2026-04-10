import { Injectable } from '@angular/core';
import { ApiServiceService } from './api/api-service.service';

@Injectable({
  providedIn: 'root'
})
export class DeviceService {

  private base = 'device';

  constructor(private api: ApiServiceService) {}

  getActiveDevices(pageNumber: number, pageSize: number) {
    return this.api.get(
      `${this.base}/active?pageNumber=${pageNumber}&pageSize=${pageSize}`
    );
  }

  getAllDevices() {
    return this.api.get(`${this.base}/all`);
  }

  getById(id: number) {
    return this.api.get(`${this.base}/${id}`);
  }

  create(data: any) {
    return this.api.post(`${this.base}`, data);
  }

  update(id: number, data: any) {
    return this.api.put(`${this.base}/${id}`, data);
  }

  delete(id: number) {
    return this.api.delete(`${this.base}/${id}`);
  }
}
