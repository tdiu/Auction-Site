import {Component, inject} from '@angular/core';
import {AuctionService} from '../../../core/services/auction-service';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {BehaviorSubject, combineLatest, switchMap} from 'rxjs';
import {AsyncPipe, DatePipe} from '@angular/common';
import {BidService} from '../../../core/services/bid-service';
import {ToastService} from '../../../core/services/toast-service';

import {AccountService} from '../../../core/services/account-service';

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

  private refreshAuction$ = new BehaviorSubject<void>(undefined);
  showBids = false;

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

    this.bidService.createBid(auctionId, {amount: parseFloat(amount)}).subscribe({
      next: () => {
        this.toastService.success('Bid placed successfully');
        this.refreshAuction$.next();
      },
      error: error => {
        this.toastService.error(error.error || 'Failed to place bid');
      }
    });
  }
}
