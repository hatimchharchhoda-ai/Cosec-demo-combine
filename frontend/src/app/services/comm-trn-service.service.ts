import { Injectable } from '@angular/core';
import { ApiServiceService } from './api/api-service.service';

@Injectable({
  providedIn: 'root'
})
export class CommTrnServiceService {
  private base = 'commtrn';

  constructor(private api: ApiServiceService) { }

  syncUserToDevice(userId: string, deviceId: number) {
    return this.api.post(`${this.base}`, {
      userId: userId,
      deviceId: deviceId
    });
  }
}
