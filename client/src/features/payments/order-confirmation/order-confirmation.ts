import {Component, inject, OnDestroy, OnInit, signal} from '@angular/core';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {DatePipe} from '@angular/common';
import {Subscription, switchMap, timer} from 'rxjs';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {PaymentService} from '../../../core/services/payment-service';
import {AuctionService} from '../../../core/services/auction-service';
import {PaymentStatusDto} from '../../../types/payment';
import {Auction} from '../../../types/auction';

type View = 'processing' | 'paid' | 'failed' | 'timeout';

@Component({
  selector: 'app-order-confirmation',
  imports: [DatePipe, RouterLink],
  templateUrl: './order-confirmation.html',
  styleUrl: './order-confirmation.css',
})
export class OrderConfirmation implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private paymentService = inject(PaymentService);
  private auctionService = inject(AuctionService);

  // We poll by auctionId; getStatus is owner-checked server-side, so a shared or bookmarked URL
  // only ever reveals the viewer's own payment. Stripe's ?session_id is vestigial here - kept for
  // debugging only, never read for status.
  protected auctionId = this.route.snapshot.paramMap.get('auctionId')!;
  protected status = signal<PaymentStatusDto | null>(null);
  protected auction = signal<Auction | null>(null);
  protected view = signal<View>('processing');

  private sub?: Subscription;
  private polls = 0;
  private readonly maxPolls = 12; // ~18s at a 1.5s cadence

  constructor() {
    // Item name and seller are auction facts, not payment facts, and cannot change while a payment
    // settles. Fetched once here rather than on every poll. If it fails the summary just omits them.
    this.auctionService.getAuction(this.auctionId).pipe(
      takeUntilDestroyed(),
    ).subscribe({
      next: auction => this.auction.set(auction),
      error: () => this.auction.set(null),
    });
  }

  ngOnInit() {
    // The redirect back from Stripe can beat the webhook, so poll until Paid or a terminal state.
    // Usually settles on the first response.
    this.sub = timer(0, 1500).pipe(
      switchMap(() => this.paymentService.getStatus(this.auctionId)),
    ).subscribe({
      next: status => this.apply(status),
      error: () => this.tick(), // transient errors count toward the timeout too
    });
  }

  private apply(status: PaymentStatusDto) {
    this.status.set(status);
    if (status.status === 'Paid') return this.settle('paid');
    if (status.status === 'Failed' || status.status === 'Expired') return this.settle('failed');
    this.tick();
  }

  private tick() {
    if (++this.polls >= this.maxPolls) this.settle('timeout');
  }

  private settle(view: View) {
    this.view.set(view);
    this.sub?.unsubscribe();
  }

  ngOnDestroy() {
    this.sub?.unsubscribe();
  }
}
