import {inject, Injectable} from '@angular/core';
import {HttpClient} from '@angular/common/http';
import {environment} from '../../environments/environment';
import {Auction} from '../../types/auction';

@Injectable({
  providedIn: 'root',
})
export class AuctionService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  getAuctions() {
    return this.http.get<Auction[]>(`${this.baseUrl}/auctions`);
  }

  getAuction(auctionId: string) {
    return this.http.get<Auction>(`${this.baseUrl}/auctions/${auctionId}`);
  }

}
