import {Component, inject} from '@angular/core';
import {AuctionService} from '../../core/services/auction-service';
import {AbstractControl, FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators} from '@angular/forms';
import {Router} from '@angular/router';
import {AuctionRequest} from '../../types/auction';

const buyNowPriceValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const startingPrice = control.get('startingPrice');
  const buyNowPrice = control.get('buyNowPrice');

  return buyNowPrice && startingPrice && buyNowPrice.value !== null && buyNowPrice.value < startingPrice.value
    ? { buyNowPriceTooLow: true }
    : null;
};

@Component({
  selector: 'app-sell',
  imports: [ReactiveFormsModule],
  templateUrl: './sell.html',
  styleUrl: './sell.css',
})
export class Sell {
  private auctionService = inject(AuctionService);
  private fb = inject(FormBuilder);
  private router = inject(Router);

  sellForm: FormGroup = this.fb.group({
    itemName: ['', [Validators.required, Validators.minLength(3)]],
    startingPrice: [1, [Validators.required, Validators.min(1)]],
    buyNowPrice: [null, [Validators.min(1)]],
  }, { validators: buyNowPriceValidator });

  onSubmit() {
    if (this.sellForm.valid) {
      const auctionData = this.sellForm.value as AuctionRequest;
      this.auctionService.createAuction(auctionData).subscribe({
        next: (response) => {
          this.router.navigate(['/auctions', response.auctionId]);
        },
        error: (error) => {
          console.error('Error creating auction:', error);
        }
      });
    }
  }
}
