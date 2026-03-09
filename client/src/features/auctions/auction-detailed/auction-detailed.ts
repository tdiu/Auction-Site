import {Component, inject} from '@angular/core';
import {AuctionService} from '../../../core/services/auction-service';
import {ActivatedRoute, RouterLink} from '@angular/router';
import {switchMap} from 'rxjs';
import {AsyncPipe, DatePipe} from '@angular/common';

@Component({
  selector: 'app-auction-detailed',
  imports: [AsyncPipe, DatePipe, RouterLink],
  templateUrl: './auction-detailed.html',
  styleUrl: './auction-detailed.css',
})
export class AuctionDetailed {
  private auctionService = inject(AuctionService);
  private route = inject(ActivatedRoute);
  protected auction$ = this.route.paramMap.pipe(
    switchMap(params => this.auctionService.getAuction(params.get('auctionId')!))
  )

  onlyNumbers(event: KeyboardEvent) {
    const allowedKeys = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'Backspace', 'ArrowLeft', 'ArrowRight', 'Delete', 'Tab', 'Enter'];
    if (!allowedKeys.includes(event.key)) {
      event.preventDefault();
    }
  }
}
