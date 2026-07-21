import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {NgForm} from '@angular/forms';
import {ActivatedRoute, provideRouter, Router} from '@angular/router';
import {of, throwError} from 'rxjs';
import {AccountService} from '../../../core/services/account-service';
import {ToastService} from '../../../core/services/toast-service';
import {Login} from './login';

describe('Login', () => {
  let accountService: { currentUser: ReturnType<typeof vi.fn>; login: ReturnType<typeof vi.fn> };
  let toastService: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn> };
  let router: Router;
  let queryReturnUrl: string | null;

  const createComponent = () => TestBed.createComponent(Login).componentInstance;

  beforeEach(async () => {
    queryReturnUrl = null;
    accountService = {
      currentUser: vi.fn().mockReturnValue(null),
      login: vi.fn()
    };
    toastService = {
      error: vi.fn(),
      success: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        {provide: AccountService, useValue: accountService},
        {provide: ToastService, useValue: toastService},
        provideRouter([]),
        // Declared after provideRouter so this stub wins over the router's ActivatedRoute.
        {provide: ActivatedRoute, useValue: {snapshot: {queryParamMap: {get: () => queryReturnUrl}}}}
      ]
    }).compileComponents();

    router = TestBed.inject(Router);
  });

  it('shows ProblemDetails detail when login fails', () => {
    accountService.login.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 401,
      error: {title: 'Unauthorized', detail: 'Invalid Credentials'}
    })));

    createComponent().login({invalid: false} as NgForm);

    expect(toastService.error).toHaveBeenCalledWith('Invalid Credentials');
  });

  it('does not attempt login when the form is invalid', () => {
    createComponent().login({invalid: true} as NgForm);

    expect(accountService.login).not.toHaveBeenCalled();
  });

  it('returns home after login when arriving from an auth page', () => {
    queryReturnUrl = '/register';
    accountService.login.mockReturnValue(of({} as never));
    const navSpy = vi.spyOn(router, 'navigateByUrl');

    createComponent().login({invalid: false} as NgForm);

    expect(navSpy).toHaveBeenCalledWith('/');
  });

  it('returns to a normal returnUrl after login', () => {
    queryReturnUrl = '/auctions/5?pay=1';
    accountService.login.mockReturnValue(of({} as never));
    const navSpy = vi.spyOn(router, 'navigateByUrl');

    createComponent().login({invalid: false} as NgForm);

    expect(navSpy).toHaveBeenCalledWith('/auctions/5?pay=1');
  });

  it('rejects a non-local returnUrl and goes home', () => {
    queryReturnUrl = '//evil.com';
    accountService.login.mockReturnValue(of({} as never));
    const navSpy = vi.spyOn(router, 'navigateByUrl');

    createComponent().login({invalid: false} as NgForm);

    expect(navSpy).toHaveBeenCalledWith('/');
  });
});
