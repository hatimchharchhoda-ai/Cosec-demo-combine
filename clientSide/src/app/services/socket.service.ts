import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { CommTrn } from '../models/commtrn';
import { DeviceAuthPayload } from '../models/device';

@Injectable({
  providedIn: 'root'
})
export class SocketService {

  private socket!: WebSocket;

  // UI listeners
  private authStatus = new BehaviorSubject<boolean>(false);
  authStatus$ = this.authStatus.asObservable();

  private commTrnStream = new BehaviorSubject<CommTrn[]>([]);
  commTrn$ = this.commTrnStream.asObservable();

  connect(device: DeviceAuthPayload) {

    // TODO BACKEND: replace xxxx with actual backend port
    // TODO BACKEND: confirm ws or wss
    this.socket = new WebSocket('ws://localhost:xxxx/ws/device');

    this.socket.onopen = () => {
      console.log('WebSocket connected');

      // Send AUTH payload immediately after connection
      this.send({
        type: 'AUTH',
        payload: device
      });
    };

    this.socket.onmessage = (event) => {
      const message = JSON.parse(event.data);
      this.handleMessage(message);
    };

    this.socket.onerror = (error) => {
      console.error('WebSocket error', error);
    };

    this.socket.onclose = () => {
      console.warn('Socket closed. Trying reconnect in 5s...');
      setTimeout(() => this.connect(device), 5000);
    };
  }

  private handleMessage(message: any) {

    switch (message.type) {

      case 'AUTH_SUCCESS':
        // Server sets cookie here
        this.authStatus.next(true);
        break;

      case 'COMM_TRN_DATA':
        const data: CommTrn[] = message.payload;

        this.commTrnStream.next(data);

        // Send ACK for received TrnIDs
        const ids = data.map(x => x.TrnID);

        this.send({
          type: 'ACK',
          payload: ids
        });
        break;
    }
  }

  send(data: any) {
    if (this.socket && this.socket.readyState === WebSocket.OPEN) {
      this.socket.send(JSON.stringify(data));
    }
  }
}