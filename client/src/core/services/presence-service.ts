import {inject, Injectable, signal} from '@angular/core';
import {environment} from '../../environments/environment';
import {HubConnection, HubConnectionBuilder, HubConnectionState} from '@microsoft/signalr';
import {ToastService} from './toast-service';

@Injectable({
  providedIn: 'root',
})
export class PresenceService {
  private hubUrl = environment.hubUrl;
  private toast = inject(ToastService);
  private hubConnection?: HubConnection;
  private readonly onlineUsersSignal = signal<string[]>([]);
  readonly onlineUsers = this.onlineUsersSignal.asReadonly();
  private accessTokenFactory?: () => string | undefined;

  createHubConnection() {
    if (this.hubConnection?.state !== HubConnectionState.Disconnected && this.hubConnection) return;

    this.hubConnection = new HubConnectionBuilder()
      .withUrl(`${this.hubUrl}/presence`, {
        accessTokenFactory: () => this.accessTokenFactory?.() ?? ''
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('UserOnline', userId => {
      this.onlineUsersSignal.update(users => users.includes(userId) ? users : [...users, userId]);
    })

    this.hubConnection.on('UserOffline', userId => {
      this.onlineUsersSignal.update(users => users.filter(x => x !== userId));
    });

    this.hubConnection.on('GetOnlineUsers', (userIds: string[]) => {
      this.onlineUsersSignal.set(userIds);
    });

    this.hubConnection.start()
      .catch(err => {console.log(err)})
  }

  isOnline(userId: string): boolean {
    return this.onlineUsers().includes(userId);
  }

  get isConnected() {
    return this.hubConnection?.state === HubConnectionState.Connected;
  }

  async stopHubConnection() {
    if (this.hubConnection?.state !== HubConnectionState.Disconnected) {
      this.hubConnection?.stop().catch(err => {console.log(err)});
    }
    this.hubConnection = undefined;
    this.onlineUsersSignal.set([]);
  }

  setAccessTokenFactory(factory: () => string | undefined) {
    this.accessTokenFactory = factory;
  }

}
