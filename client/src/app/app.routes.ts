import { Routes } from '@angular/router';
import {Home} from '../features/home/home';
import {UserList} from '../features/users/user-list/user-list';
import {UserDetailed} from '../features/users/user-detailed/user-detailed';
import {Sell} from '../features/sell/sell';
import {Messages} from '../features/messages/messages';
import {authGuard} from '../core/guards/auth-guard';

export const routes: Routes = [
  {path: '', component: Home},
  {
    path: '',
    runGuardsAndResolvers: 'always',
    canActivate: [authGuard],
    children: [
      {path: 'sell', component: Sell},
      {path: 'messages', component: Messages},
    ]
  },
  {path: 'users', component: UserList},
  {path: 'users/:id', component: UserDetailed},

  {path: '**', component: Home},
];
