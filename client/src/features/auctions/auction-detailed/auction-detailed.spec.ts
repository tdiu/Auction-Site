import {HttpErrorResponse} from '@angular/common/http';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, provideRouter, Router} from '@angular/router';
import {of, throwError} from 'rxjs';
import {AccountService} from '../../../core/services/account-service';
import {AuctionService} from '../../../core/services/auction-service';
import {BidService} from '../../../core/services/bid-service';
import {PaymentService} from '../../../core/services/payment-service';
import {ToastService} from '../../../core/services/toast-service';
import {AuctionDetailed} from './auction-detailed';

describe('AuctionDetailed', () => {
  let fixture: ComponentFixture<AuctionDetailed>;
  let component: AuctionDetailed;
  let bidService: { createBid: ReturnType<typeof vi.fn>; getBids: ReturnType<typeof vi.fn> };
  let paymentService: { createCheckoutSession: ReturnType<typeof vi.fn>; getStatus: ReturnType<typeof vi.fn> };
  let toastService: {
    error: ReturnType<typeof vi.fn>;
    success: ReturnType<typeof vi.fn>;
    info: ReturnType<typeof vi.fn>;
  };
  let navigate: ReturnType<typeof vi.spyOn>;

  // Query params drive the pay deep-link and the cancelled-checkout toast, so each test picks its own.
  async function setup(queryParams: Record<string, string> = {}) {
    bidService = {
      createBid: vi.fn(),
      getBids: vi.fn().mockReturnValue(of([]))
    };
    paymentService = {
      createCheckoutSession: vi.fn().mockReturnValue(of({checkoutUrl: 'https://checkout.stripe.test/pay/cs_1'})),
      getStatus: vi.fn().mockReturnValue(of({status: 'Pending', amount: 150}))
    };
    toastService = {
      error: vi.fn(),
      success: vi.fn(),
      info: vi.fn()
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
        {provide: PaymentService, useValue: paymentService},
        {provide: ToastService, useValue: toastService},
        {provide: AccountService, useValue: {currentUser: vi.fn().mockReturnValue(null)}},
        provideRouter([]),
        // Declared after provideRouter so this stub wins over the router's own ActivatedRoute.
        {
          provide: ActivatedRoute,
          useValue: {
            paramMap: of(convertToParamMap({auctionId: '1'})),
            queryParamMap: of(convertToParamMap(queryParams)),
            snapshot: {queryParamMap: convertToParamMap(queryParams)}
          }
        }
      ]
    }).compileComponents();

    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(AuctionDetailed);
    component = fixture.componentInstance;
  }

  it('shows ProblemDetails detail when bid placement fails', async () => {
    await setup();
    bidService.createBid.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409,
      error: {title: 'Conflict', detail: 'Bid is too low'}
    })));

    component.placeBid('1', '99');

    expect(toastService.error).toHaveBeenCalledWith('Bid is too low');
  });

  it('chains a successful buy-now bid straight into a checkout session', async () => {
    await setup();
    bidService.createBid.mockReturnValue(of({bidId: '1', bidAmount: 500}));

    component.buyNow('1', 500);

    expect(bidService.createBid).toHaveBeenCalledWith('1', {amount: 500});
    expect(paymentService.createCheckoutSession).toHaveBeenCalledWith('1');
  });

  it('does not start a checkout session when the buy-now bid fails', async () => {
    await setup();
    bidService.createBid.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 409,
      error: {title: 'Conflict', detail: 'Auction already ended'}
    })));

    component.buyNow('1', 500);

    expect(paymentService.createCheckoutSession).not.toHaveBeenCalled();
    expect(toastService.error).toHaveBeenCalledWith('Auction already ended');
  });

  it('acknowledges a cancelled checkout and strips the query param', async () => {
    await setup({cancelled: 'true'});

    expect(toastService.info).toHaveBeenCalledWith('Checkout cancelled. You can pay any time from this page.');
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: {cancelled: null},
      replaceUrl: true
    }));
  });

  it('sends a logged-out winner arriving from the pay deep-link to login', async () => {
    await setup({pay: '1'});
    TestBed.tick(); // flush the redirect effect

    expect(navigate).toHaveBeenCalledWith(['/login'], expect.objectContaining({
      queryParams: expect.objectContaining({returnUrl: expect.any(String)})
    }));
  });
});
