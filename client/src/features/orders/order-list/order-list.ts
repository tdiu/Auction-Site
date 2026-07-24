import {Component, inject, signal} from '@angular/core';
import {RouterLink} from '@angular/router';
import {DatePipe} from '@angular/common';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {OrderService} from '../../../core/services/order-service';
import {Order} from '../../../types/order';

@Component({
  selector: 'app-order-list',
  imports: [DatePipe, RouterLink],
  templateUrl: './order-list.html',
})
export class OrderList {
  private orderService = inject(OrderService);

  protected orders = signal<Order[]>([]);

  constructor() {
    // The server already filters to the caller's own payments and orders them newest first.
    this.orderService.getOrders().pipe(
      takeUntilDestroyed(),
    ).subscribe({
      next: orders => this.orders.set(orders),
      error: () => this.orders.set([]),
    });
  }
}
