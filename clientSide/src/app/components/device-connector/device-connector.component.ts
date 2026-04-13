import { Component } from '@angular/core';
<<<<<<< HEAD
import { SocketService } from '../../services/socket.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CommTrn } from '../../models/commtrn';
import { DeviceAuthPayload } from '../../models/device';
=======
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { PollService } from '../../services/poll.service';
import { TrnItemDto } from '../../models/poll';
>>>>>>> 89794c4 (WIP: my local changes before merging server branch)

@Component({
  selector: 'app-device-connector',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './device-connector.component.html',
  styleUrl: './device-connector.component.css'
})
export class DeviceConnectorComponent {
<<<<<<< HEAD
  device: DeviceAuthPayload = {
=======
  device = {
>>>>>>> 89794c4 (WIP: my local changes before merging server branch)
    DeviceID: 0,
    MACAddr: '',
    IPAddr: ''
  };

<<<<<<< HEAD
  isAuthenticated = false;
  commTrnList: CommTrn[] = [];

  constructor(private socketService: SocketService) {

    this.socketService.authStatus$.subscribe(status => {
      this.isAuthenticated = status;
    });

    this.socketService.commTrn$.subscribe(data => {
=======
  commTrnList: TrnItemDto[] = [];
  isLoggedIn = false;

  constructor(
    private auth: AuthService,
    private poll: PollService
  ) {
    this.poll.trn$.subscribe(data => {
>>>>>>> 89794c4 (WIP: my local changes before merging server branch)
      this.commTrnList = [...this.commTrnList, ...data];
    });
  }

  connectDevice() {
<<<<<<< HEAD
    this.socketService.connect(this.device);
=======
    this.auth.login(this.device).subscribe({
      next: () => {
        this.isLoggedIn = true;
        this.poll.startPolling();
      },
      error: err => console.error(err)
    });
>>>>>>> 89794c4 (WIP: my local changes before merging server branch)
  }
}
