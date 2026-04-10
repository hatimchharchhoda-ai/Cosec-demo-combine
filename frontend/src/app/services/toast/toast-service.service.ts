import { Injectable } from '@angular/core';
import { ToastrService } from 'ngx-toastr';

@Injectable({
  providedIn: 'root'
})
export class ToastServiceService {

  constructor(private toastr: ToastrService) {}

  success(msg: string) {
    this.toastr.success(msg);
  }

  error(msg: string) {
    this.toastr.error(msg);
  }
}
