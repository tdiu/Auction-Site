import {HttpContextToken, HttpErrorResponse, HttpInterceptorFn} from '@angular/common/http';
import {catchError, EMPTY, finalize, map, Observable, shareReplay, switchMap, throwError} from 'rxjs';
import {inject} from '@angular/core';
import {NavigationExtras, Router} from '@angular/router';
import {AccountService} from '../services/account-service';
import {User} from '../../types/user';

const RETRIED_AFTER_REFRESH = new HttpContextToken<boolean>(() => false);
let refreshRequest$: Observable<User> | null = null;
function getPathname(url: string) {
  return new URL(url, window.location.origin).pathname;
}

function isAccountEndpoint(url: string) {
  const pathname = getPathname(url);

  return pathname.endsWith('/account/login') ||
    pathname.endsWith('/account/register') ||
    pathname.endsWith('/account/refresh-token') ||
    pathname.endsWith('/account/logout');
}

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  const accountService = inject(AccountService);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      const shouldTryRefresh =
        error.status === 401 &&
        !isAccountEndpoint(req.url) &&
        !req.context.get(RETRIED_AFTER_REFRESH) &&
        !!accountService.currentUser();

      if (shouldTryRefresh) {
        refreshRequest$ ??= accountService.refreshToken().pipe(
          map(user => {
            if (!user) throw new Error('No refresh session');
            return user;
          }),
          catchError(refreshError => {
            accountService.clearCurrentUser();
            router.navigateByUrl('/');
            return EMPTY;
          }),
          finalize(() => refreshRequest$ = null),
          shareReplay(1)
        );

        return refreshRequest$.pipe(
          switchMap(() => next(req.clone({
            context: req.context.set(RETRIED_AFTER_REFRESH, true)
          })))
        );
      }

      switch (error.status) {
        case 404:
          router.navigateByUrl('/not-found');
          break;
        case 500:
          const navigationExtras: NavigationExtras = {state: {error: error.error}};
          router.navigateByUrl('/server-error', navigationExtras);
          break;
      }
      return throwError(() => error);
    })
  )
};
