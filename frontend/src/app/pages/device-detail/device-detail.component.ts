import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { DeviceService } from '../../services/device.service';
import { ToastServiceService } from '../../services/toast/toast-service.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CommTrnServiceService } from '../../services/comm-trn-service.service';

@Component({
  selector: 'app-device-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './device-detail.component.html',
  styleUrl: './device-detail.component.css'
})
export class DeviceDetailComponent implements OnInit {
  device: any | null;
  isEdit = false;
  isAddMode = false;

  constructor(
    private router: Router,
    private route: ActivatedRoute,
    private service: DeviceService,
    private toast: ToastServiceService,
    private commTrn: CommTrnServiceService,
  ) { }

  ngOnInit() {
    const idParam = this.route.snapshot.paramMap.get('id');

    // ADD MODE
    if (!idParam) {
      this.isAddMode = true;
      this.isEdit = true;

      this.device = {
        deviceName: '',
        macAddr: '',
        ipAddr: '',
        deviceType: '',
        typeMID: '',
      };

      return;
    }

    // VIEW / EDIT MODE
    const id = Number(idParam);
    this.service.getById(id).subscribe((res: any) => {
      this.device = res.data;
    });
  }

  edit() {
    this.isEdit = true;
  }

  update() {
    if (!this.device) return;

    this.service.update(this.device.deviceID, this.device).subscribe(() => {
      this.toast.success('Device updated successfully');
      this.isEdit = false;
    });
  }

  delete() {
    if (!this.device) return;

    this.service.delete(this.device.deviceID).subscribe(() => {
      this.toast.success('Device deleted');
      this.router.navigate(['/device']);
    });
  }

  back() {
    this.router.navigate(['/device']);
  }

  save() {
    this.service.create(this.device).subscribe(() => {
      this.toast.success('Device created successfully');
      this.router.navigate(['/device']);
    });
  }

  syncUser() {
    if (!this.device) return;

    const userId = localStorage.getItem('userId');
    if (!userId) {
      this.toast.error('User ID not found. Please log in again.');
      return;
    }
    
    this.commTrn
      .syncUserToDevice(this.device.deviceID, this.device.typeMID)
      .subscribe({
        next: () => {
          this.toast.success('Sync request sent to device successfully');
        },
        error: (err) => {
          this.toast.error(err.error.message || 'Sync failed');
        }
      });
  }
}