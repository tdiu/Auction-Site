import {HttpErrorResponse} from '@angular/common/http';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {of, throwError} from 'rxjs';
import {AccountService} from '../../../core/services/account-service';
import {AuctionService} from '../../../core/services/auction-service';
import {BidService} from '../../../core/services/bid-service';
import {ToastService} from '../../../core/services/toast-service';
import {AuctionDetailed} from './auction-detailed';

describe('AuctionDetailed', () => {
  let fixture: ComponentFixture<AuctionDetailed>;
  let component: AuctionDetailed;
  let bidService: { createBid: ReturnType<typeof vi.fn>; getBids: ReturnType<typeof vi.fn> };
  let toastService: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    bidService = {
      createBid: vi.fn(),
      getBids: vi.fn().mockReturnValue(of([]))
    };
    toastService = {
      error: vi.fn(),
      success: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [AuctionDetailed],
      providers: [
        {
          provide: AuctionService,
          useValue: {
            getAuction: vi.fn().mockReturnValue(of({
              auctionId: '1',
              itemName: 'Test item',
              startingPrice: 100,
              buyNowPrice: null,
              sellerId: 'seller-id',
              sellerName: 'Seller',
              sellerCreatedAt: new Date().toISOString(),
              startTime: new Date().toISOString(),
              endTime: new Date(Date.now() + 60_000).toISOString(),
              status: 'Active'
            }))
          }
        },
        {provide: BidService, useValue: bidService},
        {provide: ToastService, useValue: toastService},
        {provide: AccountService, useValue: {currentUser: vi.fn().mockReturnValue(null)}},
        {
          provide: ActivatedRoute,
          useValue: {paramMap: of(convertToParamMap({auctionId: '1'}))}
        }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(AuctionDetailed);
    component = fixture.componentInstance;
  });

  it('shows ProblemDetails detail when bid placement fails', () => {
    bidService.createBid.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409,
      error: {title: 'Conflict', detail: 'Bid is too low'}
    })));

    component.placeBid('1', '99');

    expect(toastService.error).toHaveBeenCalledWith('Bid is too low');
  });
});
