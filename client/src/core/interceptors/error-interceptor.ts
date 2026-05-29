import { HttpInterceptorFn } from '@angular/common/http';
import {catchError} from 'rxjs';
import {ToastService} from '../services/toast-service';
import {inject} from '@angular/core';
import {NavigationExtras, Router} from '@angular/router';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  return next(req).pipe(
    catchError((error) => {
      if (error) {
        switch (error.status) {
          case 400:
            break;
          case 401:
            break;
          case 404:
            router.navigateByUrl('/not-found')
            break;
          case 500:
            const navigationExtras: NavigationExtras = {state: {error: error.error}};
            router.navigateByUrl('/server-error', navigationExtras)
            break;
          default:
            break;
        }
      }
      throw error;
    })
  )
};
