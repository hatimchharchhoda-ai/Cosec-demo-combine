import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';
import { LoginRequest } from '../../models/models';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, CommonModule],
  templateUrl: './login.component.html'
})
export class LoginComponent {

  form: LoginRequest = {
    deviceId: 0,
    mACAddr:  '',
    iPAddr:   ''
  };

  loading = false;
  error   = '';

  constructor(private auth: AuthService, private router: Router) {}

  submit(): void {
    this.error   = '';
    this.loading = true;

    this.auth.login(this.form).subscribe({
      next: res => {
        this.loading = false;
        if (res.success) {
          this.router.navigate(['/dashboard']);
        } else {
          this.error = res.message;
        }
      },
      error: err => {
        this.loading = false;
        this.error   = err.error?.message ?? 'Login failed. Check server connection.';
      }
    });
  }
}
