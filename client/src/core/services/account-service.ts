import {inject, Injectable, signal} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {LoginCreds, RegisterCreds, User} from '../../types/user';
import {finalize, tap} from 'rxjs';
import {environment} from '../../environments/environment';
import {PresenceService} from './presence-service';
import {HubConnectionState} from '@microsoft/signalr';

@Injectable({
  providedIn: 'root',
})
export class AccountService {
  private http = inject(HttpClient);
  currentUser = signal<User | null>(null);
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

  setCurrentUser(user: User) {
    localStorage.setItem('user', JSON.stringify(user));
    this.currentUser.set(user);
    this.presenceService.setAccessTokenFactory(() => this.currentUser()?.token);
    if (this.presenceService.hubConnection?.state !== HubConnectionState.Connected) {
      this.presenceService.createHubConnection()
    }
  }

  clearCurrentUser() {
    localStorage.removeItem('user');
    this.currentUser.set(null);
    this.presenceService.stopHubConnection();
  }
}
