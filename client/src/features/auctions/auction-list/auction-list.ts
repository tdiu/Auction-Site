import {Component, inject} from '@angular/core';
import {AuctionService} from '../../../core/services/auction-service';
import {AsyncPipe} from '@angular/common';
import {AuctionCard} from '../auction-card/auction-card';
import {BehaviorSubject, combineLatest, switchMap} from 'rxjs';

@Component({
  selector: 'app-auction-list',
  imports: [AsyncPipe, AuctionCard],
  templateUrl: './auction-list.html',
  styleUrl: './auction-list.css',
})
export class AuctionList {
  private auctionService = inject(AuctionService);

  protected status$ = new BehaviorSubject<string>('Active');
  protected page$ = new BehaviorSubject<number>(1);

  protected response$ = combineLatest([this.status$, this.page$]).pipe(
    switchMap(([status, page]) =>
      this.auctionService.getAuctions(undefined, undefined, status, page))
  );

  setStatus(status: string) {
    this.status$.next(status);
    this.page$.next(1);
  }

  goToPage(page: number) {
    this.page$.next(page);
  }
}
