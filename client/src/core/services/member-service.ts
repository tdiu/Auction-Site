import {inject, Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {environment} from '../../environments/environment';
import {Member} from '../../types/member';

@Injectable({
  providedIn: 'root',
})
export class MemberService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  getMembers() {
    return this.http.get<Member[]>(`${this.baseUrl}/users`);
  }

  getMember(displayName: string) {
    return this.http.get<Member>(`${this.baseUrl}/users/${displayName}`);
  }
}
