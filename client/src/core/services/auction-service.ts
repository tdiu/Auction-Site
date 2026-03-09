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

  getAuctions(displayName?: string) {
    let params = {};
    if (displayName) {
      params = {displayName};
    }
    return this.http.get<Auction[]>(`${this.baseUrl}/auctions`, {params});
  }

  getAuction(auctionId: string) {
    return this.http.get<Auction>(`${this.baseUrl}/auctions/${auctionId}`);
  }

  createAuction(auctionData: {itemName: string, startingPrice: number, buyNowPrice?: number | null}) {
    return this.http.post<Auction>(`${this.baseUrl}/auctions`, auctionData);
  }
}
