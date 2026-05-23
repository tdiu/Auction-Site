import {inject, Injectable} from '@angular/core';
import {HttpClient, HttpParams} from '@angular/common/http';
import {environment} from '../../environments/environment';
import {Auction, AuctionRequest} from '../../types/auction';

@Injectable({
  providedIn: 'root',
})
export class AuctionService {
  private http = inject(HttpClient);
  private baseUrl = environment.apiUrl;

  getAuctions(displayName?: string, searchTerm?: string, status?: string) {
    let params = new HttpParams();
    if (displayName) params = params.set('displayName', displayName);
    if (searchTerm) params = params.set('searchTerm', searchTerm);
    if (status) params = params.set('status', status);

    return this.http.get<Auction[]>(`${this.baseUrl}/auctions`, {params});
  }

  getAuction(auctionId: string) {
    return this.http.get<Auction>(`${this.baseUrl}/auctions/${auctionId}`);
  }

  createAuction(auctionData: AuctionRequest) {
    return this.http.post<Auction>(`${this.baseUrl}/auctions`, auctionData);
  }
}
