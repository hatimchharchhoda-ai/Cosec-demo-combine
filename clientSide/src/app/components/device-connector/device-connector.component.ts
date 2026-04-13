import { ChangeDetectorRef, Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { PollService } from '../../services/poll.service';
import { TrnItemDto } from '../../models/poll';

@Component({
  selector: 'app-device-connector',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './device-connector.component.html',
  styleUrl: './device-connector.component.css'
})
export class DeviceConnectorComponent {
  device = {
    deviceID: 0,
    MACAddr: '',
    IPAddr: ''
  };

  commTrnList: TrnItemDto[] = [];
  isLoggedIn = false;

  constructor(
    private auth: AuthService,
    private poll: PollService,
    private cdr: ChangeDetectorRef
  ) {
    this.poll.trn$.subscribe(rows => {
      if (rows.length > 0) {
        this.processRows(rows);
      }
    });
  }

  connectDevice() {
    this.auth.login(this.device).subscribe({
      next: () => {
        this.isLoggedIn = true;
        this.poll.startPolling();
        this.cdr.markForCheck();
      },
      error: err => {
        alert(err.error?.message || 'Device authentication failed');
        console.error(err);
      }
    });
  }

  // This simulates sending rows to actual device
  private processRows(rows: TrnItemDto[]) {
    console.log('Processing rows on device...', rows);

    // simulate device time
    setTimeout(() => {
      this.commTrnList = [...this.commTrnList, ...rows];

      // ✅ ACK only AFTER processing
      this.poll.ackBatch();

      this.cdr.markForCheck();
    }, 2000);
  }
}
