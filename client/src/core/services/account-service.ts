import {inject, Injectable, signal} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {LoginCreds, RegisterCreds, User} from '../../types/user';
import {defer, finalize, firstValueFrom, from, Observable, tap} from 'rxjs';
import {environment} from '../../environments/environment';
import {PresenceService} from './presence-service';

@Injectable({
  providedIn: 'root',
})
export class AccountService {
  private http = inject(HttpClient);
  private readonly currentUserSignal = signal<User | null>(null);
  readonly currentUser = this.currentUserSignal.asReadonly();
  private presenceService = inject(PresenceService);
  private serialised<T>(work: () => Observable<T>): Observable<T> {
    if (!navigator.locks) return work();
    return defer(() =>
      from(navigator.locks
        .request<Promise<T>>('auth-refresh', () => firstValueFrom(work()))
        .then(value => value)));
  }

  private baseUrl = environment.apiUrl;

  register(creds: RegisterCreds) {
    return this.http.post<User>(`${this.baseUrl}/account/register`, creds, {
      withCredentials: true
      }).pipe(
      tap(user => {
        if (user) {
          this.setCurrentUser(user);
        }
      })
    )
  }

  login(creds: LoginCreds) {
    return this.http.post<User>(`${this.baseUrl}/account/login`, creds, {
      withCredentials: true
    }).pipe(
      tap(user => {
        if (user) {
          this.setCurrentUser(user)
        }
      })
    )
  }

  // A real navigation, not HttpClient: the browser has to follow the redirect to the provider and
  // back. Nothing else in this service changes, because the callback sets the refresh cookie and
  // the existing refreshToken() on boot picks the session up.
  //
  // provider is the Identity scheme name ('Google'), passed through as a path segment so the API
  // can hand it straight to Challenge(). Resolved against document.baseURI because prod's apiUrl
  // is the relative 'api' -- assigning that to location.href would resolve against the current
  // page instead, sending a user on /auctions/5 to /auctions/api/...
  startExternalLogin(provider: string, returnUrl?: string) {
    const url = new URL(`${this.baseUrl}/account/external-login/${provider}`, document.baseURI);
    if (returnUrl && returnUrl !== '/') url.searchParams.set('returnUrl', returnUrl);
    window.location.href = url.toString();
  }

  refreshToken() {
    return this.serialised(() =>
      this.http.post<User | null>(`${this.baseUrl}/account/refresh-token`, {}, {
        withCredentials: true
      }).pipe(
        tap(user => {
          if (user) {
            this.setCurrentUser(user)
          }
        })
      )
    );
  }

  logout() {
    return this.http.post<void>(`${this.baseUrl}/account/logout`, {}, {
      withCredentials: true
    }).pipe(
      finalize(() => {
        this.clearCurrentUser()
      })
    )
  }

  private setCurrentUser(user: User) {
    this.currentUserSignal.set(user);
    this.presenceService.setAccessTokenFactory(() => this.currentUser()?.token);
    if (!this.presenceService.isConnected) {
      this.presenceService.createHubConnection()
    }
  }

  clearCurrentUser() {
    this.currentUserSignal.set(null);
    this.presenceService.stopHubConnection();
  }
}
