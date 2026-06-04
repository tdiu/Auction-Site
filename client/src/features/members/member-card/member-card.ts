import {Component, computed, inject, input} from '@angular/core';
import {Member} from '../../../types/member';
import {RouterLink} from '@angular/router';
import {DatePipe} from '@angular/common';
import {PresenceService} from '../../../core/services/presence-service';

@Component({
  selector: 'app-member-card',
  imports: [
    RouterLink,
    DatePipe
  ],
  templateUrl: './member-card.html',
  styleUrl: './member-card.css',
})
export class MemberCard {
  member = input.required<Member>();
  private presenceService = inject(PresenceService);
  protected isOnline = computed(() => this.presenceService.onlineUsers()
    .includes(this.member().id));
}
