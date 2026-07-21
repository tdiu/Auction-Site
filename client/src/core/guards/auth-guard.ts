import { CanActivateFn, Router } from '@angular/router';
import {AccountService} from '../services/account-service';
import {inject} from '@angular/core';

export const authGuard: CanActivateFn = (_route, state) => {
  const accountService = inject(AccountService);
  const router = inject(Router);

  if (accountService.currentUser()) return true;

  return router.createUrlTree(['/login'], {queryParams: {returnUrl: state.url}});
};
