import { Injectable } from '@angular/core';
import { ApiServiceService } from './api/api-service.service';

@Injectable({
  providedIn: 'root'
})
export class CommTrnServiceService {
  private base = 'commtrn';

  constructor(private api: ApiServiceService) { }

  syncUserToDevice(deviceId: number, typeMID: string) {
    return this.api.post(`${this.base}`, {
      DeviceId: deviceId,
      TypeMID: typeMID
    });
  }
}