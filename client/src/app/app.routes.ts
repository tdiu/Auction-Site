import { Routes } from '@angular/router';
import {Home} from '../features/home/home';
import {MemberList} from '../features/members/member-list/member-list';
import {MemberDetailed} from '../features/members/member-detailed/member-detailed';
import {Sell} from '../features/sell/sell';
import {Messages} from '../features/messages/messages';
import {authGuard} from '../core/guards/auth-guard';
import {NotFound} from '../shared/errors/not-found/not-found';
import {ServerError} from '../shared/errors/server-error/server-error';
import {AuctionList} from '../features/auctions/auction-list/auction-list';

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
  {path: 'members', component: MemberList},
  {path: 'members/:displayName', component: MemberDetailed},
  {path: 'auctions', component: AuctionList},
  {path: 'server-error', component: ServerError},
  {path: '**', component: NotFound},
];
