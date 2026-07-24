import {Component, computed, inject, signal, OnDestroy} from '@angular/core';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {DatePipe} from '@angular/common';
import {Subscription, timer, switchMap} from 'rxjs';
import {OrderService} from '../../../core/services/order-service';
import {Order} from '../../../types/order';

type View = 'loading' | 'processing' | 'paid' | 'failed' | 'timeout';

@Component({
  selector: 'app-order-detail',
  imports: [DatePipe, RouterLink],
  templateUrl: './order-detail.html',
  styleUrl: './order-detail.css',
})
export class OrderDetail implements OnDestroy {
  private route = inject(ActivatedRoute);
  private orderService = inject(OrderService);

  // Keyed by the order, not the auction. getOrder is ownership-checked server-side, so a bookmarked
  // or shared URL only ever resolves for the buyer. Stripe's ?session_id is vestigial: kept for
  // debugging, never read.
  protected orderId = this.route.snapshot.paramMap.get('orderId')!;
  protected order = signal<Order | null>(null);
  protected notFound = signal(false);
  protected timedOut = signal(false);

  private sub?: Subscription;
  private polls = 0;
  private readonly maxPolls = 12; // ~18s at a 1.5s cadence

  // Terminal statuses never poll. This is what lets one page serve a fresh checkout and a
  // six-month-old receipt: history simply arrives already settled.
  private static isSettled(status: string) {
    return status === 'Paid' || status === 'Failed' || status === 'Expired';
  }

  protected view = computed<View>(() => {
    const order = this.order();
    if (!order) return 'loading';
    if (order.status === 'Paid') return 'paid';
    if (order.status === 'Failed' || order.status === 'Expired') return 'failed';
    return this.timedOut() ? 'timeout' : 'processing';
  });

  constructor() {
    // One read first. If it comes back settled (the common case, including every history visit) no
    // timer is ever created.
    this.orderService.getOrder(this.orderId).subscribe({
      next: order => {
        this.order.set(order);
        if (!OrderDetail.isSettled(order.status)) this.startPolling();
      },
      error: () => this.notFound.set(true),
    });
  }

  private startPolling() {
    this.sub = timer(1500, 1500).pipe(
      switchMap(() => this.orderService.getOrder(this.orderId)),
    ).subscribe({
      next: order => {
        this.order.set(order);
        if (OrderDetail.isSettled(order.status)) return this.stop();
        this.tick();
      },
      error: () => this.tick(), // transient errors count toward the timeout too
    });
  }

  private tick() {
    if (++this.polls >= this.maxPolls) {
      this.timedOut.set(true);
      this.stop();
    }
  }

  private stop() {
    this.sub?.unsubscribe();
  }

  ngOnDestroy() {
    this.stop();
  }
}
