import {inject, Injectable, signal} from '@angular/core';
import {environment} from '../../environments/environment';
import {ToastService} from './toast-service';
import {User} from '../../types/user';
import {HubConnection, HubConnectionBuilder, HubConnectionState} from '@microsoft/signalr';

@Injectable({
  providedIn: 'root',
})
export class PresenceService {
  private hubUrl = environment.hubUrl;
  private toast = inject(ToastService);
  public hubConnection?: HubConnection;
  public onlineUsers = signal<string[]>([]);

  createHubConnection(user: User) {
    if (this.hubConnection?.state !== HubConnectionState.Disconnected && this.hubConnection) return;

    this.hubConnection = new HubConnectionBuilder()
      .withUrl(`${this.hubUrl}/presence`, {
        accessTokenFactory: () => user.token
      })
      .withAutomaticReconnect()
      .build();

    this.hubConnection.on('UserOnline', userId => {
      this.onlineUsers.update(users => users.includes(userId) ? users : [...users, userId]);
    })

    this.hubConnection.on('UserOffline', userId => {
      this.onlineUsers.update(users => users.filter(x => x !== userId));
    });

    this.hubConnection.on('GetOnlineUsers', (userIds: string[]) => {
      this.onlineUsers.set(userIds);
    });

    this.hubConnection.start()
      .catch(err => {console.log(err)})
  }

  isOnline(userId: string): boolean {
    return this.onlineUsers().includes(userId);
  }

  stopHubConnection() {
    if (this.hubConnection?.state !== HubConnectionState.Disconnected) {
      this.hubConnection?.stop().catch(err => {console.log(err)});
    }
    this.onlineUsers.set([]);
  }

}
