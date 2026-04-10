import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { ToastServiceService } from '../../services/toast/toast-service.service';
import { Router } from '@angular/router';
import { DeviceService } from '../../services/device.service';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-device',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './device.component.html',
  styleUrl: './device.component.css'
})
export class DeviceComponent implements OnInit {
  devices: any[] = [];

  currentPage = 1;
  pageSize = 10;
  totalRecords = 0;
  totalPages = 0;
  pages: number[] = [];

  userId: number = 0;

  constructor(
    private service: DeviceService,
    private toast: ToastServiceService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) { }

  ngOnInit() {
    this.userId = Number(localStorage.getItem('userId'));
    this.loadDevices(1);
  }

  loadDevices(page: number) {
    this.service.getActiveDevices(page, this.pageSize).subscribe({
      next: (res: any) => {
        const result = res.data;

        this.devices = result.data;           // only 10 devices
        this.totalRecords = result.totalRecords;
        this.totalPages = Math.ceil(this.totalRecords / this.pageSize);
        this.currentPage = result.pageNumber;

        this.buildPages();
        this.cdr.detectChanges();
      },
      error: () => this.toast.error('Failed to load devices')
    });
  }

  buildPages() {
    const pages: number[] = [];

    const start = this.currentPage;
    const end = Math.min(this.currentPage + 2, this.totalPages);

    for (let i = start; i <= end; i++) {
      pages.push(i);
    }

    this.pages = pages;
  }

  setPage(page: number) {
    if (page !== this.currentPage) {
      this.loadDevices(page);
    }
  }

  next() {
    if (this.currentPage < this.totalPages) {
      this.loadDevices(this.currentPage + 1);
    }
  }

  prev() {
    if (this.currentPage > 1) {
      this.loadDevices(this.currentPage - 1);
    }
  }

  openDevice(device: any) {
    this.router.navigate(['/device/detail', device.deviceID], {
      state: { device }
    });
  }
}