import { Component } from '@angular/core';
import { SocketService } from '../../services/socket.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CommTrn } from '../../models/commtrn';
import { DeviceAuthPayload } from '../../models/device';

@Component({
  selector: 'app-device-connector',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './device-connector.component.html',
  styleUrl: './device-connector.component.css'
})
export class DeviceConnectorComponent {
  device: DeviceAuthPayload = {
    DeviceID: 0,
    MACAddr: '',
    IPAddr: ''
  };

  isAuthenticated = false;
  commTrnList: CommTrn[] = [];

  constructor(private socketService: SocketService) {

    this.socketService.authStatus$.subscribe(status => {
      this.isAuthenticated = status;
    });

    this.socketService.commTrn$.subscribe(data => {
      this.commTrnList = [...this.commTrnList, ...data];
    });
  }

  connectDevice() {
    this.socketService.connect(this.device);
  }
}
