import {inject, Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {environment} from '../../environments/environment';
import {Bid, BidRequest} from '../../types/bid';

@Injectable({
  providedIn: 'root',
})
export class BidService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  getBids(auctionId: string) {
    return this.http.get<Bid[]>(`${this.baseUrl}/auctions/${auctionId}/bids`);
  }

  createBid(auctionId: string, bidData: BidRequest) {
    return this.http.post<Bid>(`${this.baseUrl}/auctions/${auctionId}/bids`, bidData);
  }
}
