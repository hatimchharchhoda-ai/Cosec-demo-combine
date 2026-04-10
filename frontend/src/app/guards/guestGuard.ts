import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthServiceService } from '../services/auth/auth-service.service';

export const guestGuard: CanActivateFn = () => {
  const authService = inject(AuthServiceService);
  const router = inject(Router);

  return authService.checkAuth().pipe(
    map(() => {
      router.navigate(['/device']);
      return false;
    }),
    catchError(() => {
      return of(true);
    })
  );
};