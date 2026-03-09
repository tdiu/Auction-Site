import {Component, inject} from '@angular/core';
import {AuctionService} from '../../../core/services/auction-service';
import {AsyncPipe} from '@angular/common';
import {AuctionCard} from '../auction-card/auction-card';

@Component({
  selector: 'app-auction-list',
  imports: [AsyncPipe, AuctionCard],
  templateUrl: './auction-list.html',
  styleUrl: './auction-list.css',
})
export class AuctionList {
  private auctionService = inject(AuctionService);
  protected auctions$ = this.auctionService.getAuctions();
}
