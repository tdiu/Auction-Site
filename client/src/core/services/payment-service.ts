import {inject, Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {environment} from '../../environments/environment';
import {CreatePaymentResponse, PaymentStatusDto} from '../../types/payment';

@Injectable({
  providedIn: 'root',
})
export class PaymentService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  createCheckoutSession(auctionId: string) {
    return this.http.post<CreatePaymentResponse>(`${this.baseUrl}/payments/${auctionId}/checkout-session`, {});
  }

  getStatus(auctionId: string) {
    return this.http.get<PaymentStatusDto>(`${this.baseUrl}/payments/${auctionId}/status`);
  }
}
