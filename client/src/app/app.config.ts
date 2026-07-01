import {ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners} from '@angular/core';
import {provideRouter, withViewTransitions} from '@angular/router';

import { routes } from './app.routes';
import {provideHttpClient, withInterceptors} from '@angular/common/http';
import {InitService} from '../core/services/init-service';
import {lastValueFrom} from 'rxjs';
import {errorInterceptor} from '../core/interceptors/error-interceptor';
import {jwtInterceptor} from '../core/interceptors/jwt-interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withViewTransitions()),
    provideHttpClient(withInterceptors([errorInterceptor, jwtInterceptor])),
    provideAppInitializer(async () => {
      const initService = inject(InitService);

      try {
        await lastValueFrom(initService.init());
      } finally {
        const splash = document.getElementById('initial-splash');
        if (splash) {
          splash.remove();
        }
      }
    })
  ]
};
