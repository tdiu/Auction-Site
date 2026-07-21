import {Component, inject} from '@angular/core';
import {AuctionService} from '../../../core/services/auction-service';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {BehaviorSubject, combineLatest, finalize, map, switchMap, timer} from 'rxjs';
import {AsyncPipe, DatePipe} from '@angular/common';
import {BidService} from '../../../core/services/bid-service';
import {ToastService} from '../../../core/services/toast-service';
import {PresenceService} from '../../../core/services/presence-service';
import {AccountService} from '../../../core/services/account-service';
import {getApiErrorMessage} from '../../../types/error';
import {Auction} from '../../../types/auction';

@Component({
  selector: 'app-auction-detailed',
  imports: [AsyncPipe, RouterLink, DatePipe],
  templateUrl: './auction-detailed.html',
  styleUrl: './auction-detailed.css',
})
export class AuctionDetailed {
  private auctionService = inject(AuctionService);
  private bidService = inject(BidService);
  private toastService = inject(ToastService);
  private route = inject(ActivatedRoute);
  protected accountService = inject(AccountService);
  protected presenceService = inject(PresenceService);


  private refreshAuction$ = new BehaviorSubject<void>(undefined);
  showBids = false;
  isPlacingBid = false;
  isBuyingNow = false;

  protected auction$ = combineLatest([
    this.route.paramMap,
    this.refreshAuction$
  ]).pipe(
    switchMap(([params]) => this.auctionService.getAuction(params.get('auctionId')!))
  );

  protected bids$ = combineLatest([
    this.route.paramMap,
    this.refreshAuction$
  ]).pipe(
    switchMap(([params]) => this.bidService.getBids(params.get('auctionId')!))
  );

  protected timeLeft$ = combineLatest([this.auction$, timer(0, 1000)]).pipe(
    map(([auction]) => this.calculateTimeLeft(auction))
  );

  private calculateTimeLeft(auction: Auction): string {
    const end = new Date(auction.endTime).getTime();
    const now = new Date().getTime();
    const diff = end - now;

    if (diff <= 0) return 'Ended';

    const days = Math.floor(diff / (1000 * 60 * 60 * 24));
    const hours = Math.floor((diff % (1000 * 60 * 60 * 24)) / (1000 * 60 * 60));
    const mins = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60));
    const secs = Math.floor((diff % (1000 * 60)) / 1000);

    if (days > 0) return `${days}d ${hours}h ${mins}m ${secs}s`;
    return `${hours}h ${mins}m ${secs}s`;
  }

  onlyNumbers(event: KeyboardEvent) {
    const allowedKeys = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'Backspace', 'ArrowLeft', 'ArrowRight', 'Delete', 'Tab', 'Enter'];
    if (!allowedKeys.includes(event.key)) {
      event.preventDefault();
    }
  }

  placeBid(auctionId: string, amount: string) {
    if (!amount) {
      this.toastService.error('Please enter a bid amount');
      return;
    }

    this.isPlacingBid = true;
    this.bidService.createBid(auctionId, {amount: parseFloat(amount)}).pipe(
      finalize(() => this.isPlacingBid = false)
    ).subscribe({
      next: () => {
        this.toastService.success('Bid placed successfully');
        this.refreshAuction$.next();
      },
      error: error => {
        this.toastService.error(getApiErrorMessage(error, 'Failed to place bid'));
      }
    });
  }

  buyNow(auctionId: string, buyNowPrice: number) {
    this.isBuyingNow = true;
    this.bidService.createBid(auctionId, {amount: buyNowPrice}).pipe(
      finalize(() => this.isBuyingNow = false)
    ).subscribe({
      next: () => {
        this.toastService.success('Purchased successfully');
        this.refreshAuction$.next();
      },
      error: error => {
        this.toastService.error(getApiErrorMessage(error, 'Failed to complete purchase'));
      }
    });
  }
}
