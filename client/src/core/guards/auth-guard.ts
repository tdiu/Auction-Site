import { CanActivateFn, Router } from '@angular/router';
import {AccountService} from '../services/account-service';
import {inject} from '@angular/core';
import {ToastService} from '../services/toast-service';

export const authGuard: CanActivateFn = (_route, state) => {
  const accountService = inject(AccountService);
  const toast = inject(ToastService);
  const router = inject(Router);

  if (accountService.currentUser()) return true;

  toast.error('Must be logged in');
  // Send them to the login page, returning to the page they wanted once signed in.
  return router.createUrlTree(['/login'], {queryParams: {returnUrl: state.url}});
};
