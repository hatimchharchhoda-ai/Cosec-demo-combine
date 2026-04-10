import { Routes } from '@angular/router';
import { LoginComponent } from './pages/auth/login/login.component';
import { DeviceComponent } from './pages/device/device.component';
import { DeviceDetailComponent } from './pages/device-detail/device-detail.component';
import { authGuard } from './guards/authGuard';
import { guestGuard } from './guards/guestGuard';
import { UserListComponent } from './pages/userlist/userlist.component';
import { UserFormComponent } from './pages/userform/userform.component';

export const routes: Routes = [
    {
        path: '',
        canActivate: [guestGuard],
        component: LoginComponent
    },

    {
        path: 'device',
        canActivate: [authGuard],
        children: [
            { path: '', component: DeviceComponent },
            { path: 'add', component: DeviceDetailComponent },
            { path: 'detail/:id', component: DeviceDetailComponent }
        ]
    },

    { path: 'user-form', component: UserFormComponent, canActivate: [authGuard] },
    { path: 'user-list', component: UserListComponent, canActivate: [authGuard] },

    { path: '**', redirectTo: '' }
];