import {inject, Injectable, signal} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {LoginCreds, RegisterCreds, User} from '../../types/user';
import {finalize, tap} from 'rxjs';
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

  refreshToken(){
    return this.http.post<User | null>(`${this.baseUrl}/account/refresh-token`, {}, {
      withCredentials: true
    }).pipe(
      tap(user => {
        if (user) {
          this.setCurrentUser(user)
        }
      })
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
