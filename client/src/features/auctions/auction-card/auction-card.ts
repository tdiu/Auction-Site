import {Component, computed, inject, input} from '@angular/core';
import {Auction} from '../../../types/auction';
import {RouterLink} from '@angular/router';
import {map, timer} from 'rxjs';
import {AsyncPipe} from '@angular/common';
import {Member} from '../../../types/member';
import {PresenceService} from '../../../core/services/presence-service';

@Component({
  selector: 'app-auction-card',
  imports: [RouterLink, AsyncPipe],
  templateUrl: './auction-card.html',
  styleUrl: './auction-card.css',
})
export class AuctionCard {
  auction = input.required<Auction>();
  size = input<'sm' | 'md'>('md');

  timeLeft$ = timer(0, 1000).pipe(
    map(() => this.calculateTimeLeft())
  );

  private calculateTimeLeft(): string {
    const end = new Date(this.auction().endTime).getTime();
    const now = new Date().getTime();
    const diff = end - now;

    if (diff <= 0) return 'Ended';

    const days = Math.floor(diff / (1000 * 60 * 60 * 24));
    const hours = Math.floor((diff % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
    const mins = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));
    const secs = Math.floor((diff % (1000 * 60)) / 1000);

    if (days > 0) return `${days}d ${hours}h ${mins}m`;
    return `${hours}h ${mins}m ${secs}s`;
  }
}
