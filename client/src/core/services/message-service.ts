import {inject, Injectable} from '@angular/core';
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

  getMessages(container: string, pageNumber: number, pageSize: number) {
    let params = new HttpParams();
    params = params.append('page', pageNumber);
    params = params.append('pageSize', pageSize);
    params = params.append('container', container);

    return this.http.get<PagedResponse<Message>>(`${this.baseUrl}/messages/`, {params});
  }

  getThread(recipientId: string) {
    return this.http.get<Message[]>(`${this.baseUrl}/messages/thread/${recipientId}`);
  }
}

