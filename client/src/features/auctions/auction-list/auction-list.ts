import {Component, inject} from '@angular/core';
import {AuctionService} from '../../../core/services/auction-service';
import {AsyncPipe} from '@angular/common';
import {AuctionCard} from '../auction-card/auction-card';
import {BehaviorSubject, switchMap} from 'rxjs';

@Component({
  selector: 'app-auction-list',
  imports: [AsyncPipe, AuctionCard],
  templateUrl: './auction-list.html',
  styleUrl: './auction-list.css',
})
export class AuctionList {
  private auctionService = inject(AuctionService);
  
  protected status$ = new BehaviorSubject<string>('Active'); 
  
  protected auctions$ = this.status$.pipe(
    switchMap(status => this.auctionService.getAuctions(undefined, undefined, status))
  );

  setStatus(status: string) {
    this.status$.next(status);
  }
}
