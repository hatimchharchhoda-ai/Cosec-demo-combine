import { ChangeDetectorRef, Component } from '@angular/core';
import { AuthServiceService } from '../../../services/auth/auth-service.service';
import { ToastServiceService } from '../../../services/toast/toast-service.service';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.css'
})
export class LoginComponent {
  loginUserID = '';
  loginPassword = '';
  isLoading = false;

  constructor(
    private auth: AuthServiceService,
    private toast: ToastServiceService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  login() {
    if (!this.loginUserID || !this.loginPassword) {
      this.toast.error('Enter User ID and Password');
      return;
    }

    this.isLoading = true;
    this.cdr.detectChanges();

    this.auth.login({
      loginUserID: this.loginUserID,
      loginPassword: this.loginPassword
    }).subscribe({
      next: (res: any) => {
        const userId = res.data.userId;
        
        this.toast.success('Login Successful');
        localStorage.setItem('userId', userId);
        this.isLoading = false;
        this.cdr.detectChanges();
        this.router.navigate(['/device']);
      },
      error: (err) => {
        this.toast.error(err.error?.message || 'Login Failed');
        this.isLoading = false;
        this.cdr.detectChanges();
      }
    });
  }
}
