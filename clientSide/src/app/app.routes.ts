import { Routes } from '@angular/router';

export const routes: Routes = [
    { path: '', loadComponent: () => import('./components/device-connector/device-connector.component').then(m => m.DeviceConnectorComponent) },
    { path: '**', redirectTo: '' }
];
