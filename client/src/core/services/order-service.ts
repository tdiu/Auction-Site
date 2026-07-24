import {inject, Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {environment} from '../../environments/environment';
import {Order} from '../../types/order';

@Injectable({
  providedIn: 'root',
})
export class OrderService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  // Backed by the payments controller. Payment is the order aggregate; the client speaks "order"
  // because that is the word buyers use, while the server keeps its own vocabulary.
  getOrders() {
    return this.http.get<Order[]>(`${this.baseUrl}/payments`);
  }

  getOrder(orderId: string) {
    return this.http.get<Order>(`${this.baseUrl}/payments/${orderId}`);
  }
}
