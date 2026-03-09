import {Component, input} from '@angular/core';
import {Auction} from '../../../types/auction';
import {RouterLink} from '@angular/router';

@Component({
  selector: 'app-auction-card',
  imports: [RouterLink],
  templateUrl: './auction-card.html',
  styleUrl: './auction-card.css',
})
export class AuctionCard {
  auction = input.required<Auction>();
}
