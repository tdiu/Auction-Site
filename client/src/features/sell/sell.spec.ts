import {HttpErrorResponse} from '@angular/common/http';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {throwError} from 'rxjs';
import {AuctionService} from '../../core/services/auction-service';
import {ToastService} from '../../core/services/toast-service';
import {Sell} from './sell';

describe('Sell', () => {
  let fixture: ComponentFixture<Sell>;
  let component: Sell;
  let auctionService: { createAuction: ReturnType<typeof vi.fn> };
  let toastService: { error: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    auctionService = {createAuction: vi.fn()};
    toastService = {error: vi.fn()};

    await TestBed.configureTestingModule({
      imports: [Sell],
      providers: [
        {provide: AuctionService, useValue: auctionService},
        {provide: ToastService, useValue: toastService},
        {provide: Router, useValue: {navigate: vi.fn()}}
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(Sell);
    component = fixture.componentInstance;
  });

  it('shows flattened validation errors when auction creation fails', () => {
    auctionService.createAuction.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: {
        title: 'Validation Failed',
        detail: 'One or more validation errors occurred',
        errors: {
          itemName: ['Item name is required'],
          startingPrice: ['Starting price must be greater than 0']
        }
      }
    })));

    component.sellForm.setValue({
      itemName: 'Valid item',
      startingPrice: 100,
      buyNowPrice: null
    });
    component.onSubmit();

    expect(toastService.error).toHaveBeenCalledWith(
      'Item name is required\nStarting price must be greater than 0'
    );
  });
});
