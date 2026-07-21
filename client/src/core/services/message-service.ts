import {computed, inject, Injectable, signal} from '@angular/core';
import {environment} from '../../environments/environment';
import {HttpClient, HttpParams} from '@angular/common/http';
import {Message} from '../../types/message';
import {PagedResponse} from '../../types/pagination';

@Injectable({
  providedIn: 'root',
})
export class MessageService {
  private baseUrl = environment.apiUrl;
  private http = inject(HttpClient);

  // Number of unread messages in the current user's inbox; drives the nav indicator.
  unreadCount = signal(0);
  hasUnread = computed(() => this.unreadCount() > 0);

  getMessages(container: string, pageNumber: number, pageSize: number) {
    return this.http.get<PagedResponse<Message>>(`${this.baseUrl}/messages/`, {
      params: this.buildParams(container, pageNumber, pageSize),
    });
  }

  // Marks a single received message as read on the server.
  markAsRead(messageId: string) {
    return this.http.put<void>(`${this.baseUrl}/messages/${messageId}/read`, {});
  }

  // Refreshes the unread indicator from the exact server-side count.
  refreshUnread() {
    this.http.get<number>(`${this.baseUrl}/messages/unread-count`).subscribe({
      next: count => this.unreadCount.set(count),
    });
  }

  private buildParams(container: string, pageNumber: number, pageSize: number) {
    let params = new HttpParams();
    params = params.append('page', pageNumber);
    params = params.append('pageSize', pageSize);
    params = params.append('container', container);
    return params;
  }

  getThread(recipientId: string) {
    return this.http.get<Message[]>(`${this.baseUrl}/messages/thread/${recipientId}`);
  }

  createMessage(recipientId: string, content: string) {
    return this.http.post<Message>(`${this.baseUrl}/messages`, {recipientId, content});
  }
}

