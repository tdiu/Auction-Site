import {Component, DestroyRef, effect, inject, signal} from '@angular/core';
import {AuctionService} from '../../../core/services/auction-service';
import {ActivatedRoute, Router, RouterLink} from '@angular/router';
import {BehaviorSubject, combineLatest, finalize, map, shareReplay, switchMap, timer} from 'rxjs';
import {takeUntilDestroyed, toSignal} from '@angular/core/rxjs-interop';
import {AsyncPipe, DatePipe} from '@angular/common';
import {BidService} from '../../../core/services/bid-service';
import {ToastService} from '../../../core/services/toast-service';
import {PresenceService} from '../../../core/services/presence-service';
import {AccountService} from '../../../core/services/account-service';
import {PaymentService} from '../../../core/services/payment-service';
import {getApiErrorMessage} from '../../../types/error';
import {Auction} from '../../../types/auction';
import {PaymentStatusDto} from '../../../types/payment';

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
  private paymentService = inject(PaymentService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroyRef = inject(DestroyRef);
  protected accountService = inject(AccountService);
  protected presenceService = inject(PresenceService);


  private refreshAuction$ = new BehaviorSubject<void>(undefined);
  showBids = false;
  isPlacingBid = false;
  isBuyingNow = false;
  isPaying = false;

  protected paymentStatus = signal<PaymentStatusDto | null>(null);

  // shareReplay is load-bearing: auction$ is cold, and the template's async pipe, timeLeft$ and the
  // payment status load below would otherwise each fire their own GET for the same auction.
  protected auction$ = combineLatest([
    this.route.paramMap,
    this.refreshAuction$
  ]).pipe(
    switchMap(([params]) => this.auctionService.getAuction(params.get('auctionId')!)),
    shareReplay({bufferSize: 1, refCount: true})
  );

  // The winner email deep-links to ?pay=1. That only emphasises the pay panel - visibility is gated
  // on winner + ended, so the panel shows for a winner who navigates here normally too.
  protected highlightPay = toSignal(
    this.route.queryParamMap.pipe(map(params => params.get('pay') === '1')),
    {initialValue: false}
  );

  constructor() {
    // Load payment status once the auction resolves so a winner who has already paid sees
    // "payment complete" instead of a Pay button that would 409.
    this.auction$.pipe(takeUntilDestroyed()).subscribe(auction => {
      const currentUser = this.accountService.currentUser();
      const hasEnded = new Date(auction.endTime).getTime() <= Date.now();
      if (hasEnded && currentUser?.id === auction.currentHighBidderId) {
        this.loadPaymentStatus(auction.auctionId);
      }
    });

    // Unauthenticated winner arriving from the email deep-link: send them to login and back here.
    // currentUser is settled by provideAppInitializer before this runs, so it won't misfire.
    effect(() => {
      if (this.highlightPay() && !this.accountService.currentUser()) {
        this.router.navigate(['/login'], {queryParams: {returnUrl: this.router.url}});
      }
    });

    // Stripe's CancelUrl lands here with ?cancelled=true. Acknowledge it, then strip the param so a
    // refresh or back-navigation doesn't toast again.
    if (this.route.snapshot.queryParamMap.get('cancelled') === 'true') {
      this.toastService.info('Checkout cancelled. You can pay any time from this page.');
      this.router.navigate([], {
        relativeTo: this.route,
        queryParams: {cancelled: null},
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  private loadPaymentStatus(auctionId: string) {
    this.paymentService.getStatus(auctionId).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: status => this.paymentStatus.set(status),
      error: () => this.paymentStatus.set(null) // 404 = no payment started yet
    });
  }

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
    // The buy-now bid ends the auction and marks the buyer the winner, which is exactly the
    // precondition CreateCheckoutSession checks, so we can chain straight into it.
    // If the bid lands but the session call fails they are simply a won-but-unpaid winner and can
    // pay from the panel below - the same recoverable state as winning a normal bid.
    this.bidService.createBid(auctionId, {amount: buyNowPrice}).pipe(
      switchMap(() => this.paymentService.createCheckoutSession(auctionId)),
      finalize(() => this.isBuyingNow = false)
    ).subscribe({
      next: res => window.location.href = res.checkoutUrl,
      error: error => {
        this.refreshAuction$.next();
        this.toastService.error(getApiErrorMessage(error, 'Could not start checkout'));
      }
    });
  }

  payNow(auctionId: string) {
    this.isPaying = true;
    this.paymentService.createCheckoutSession(auctionId).pipe(
      finalize(() => this.isPaying = false)
    ).subscribe({
      next: res => window.location.href = res.checkoutUrl,
      error: error => {
        this.toastService.error(getApiErrorMessage(error, 'Could not start checkout'));
      }
    });
  }
}
