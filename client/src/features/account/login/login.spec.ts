import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {NgForm} from '@angular/forms';
import {ActivatedRoute, provideRouter, Router} from '@angular/router';
import {of, throwError} from 'rxjs';
import {AccountService} from '../../../core/services/account-service';
import {ToastService} from '../../../core/services/toast-service';
import {Login} from './login';

describe('Login', () => {
  let accountService: {
    currentUser: ReturnType<typeof vi.fn>;
    login: ReturnType<typeof vi.fn>;
    startExternalLogin: ReturnType<typeof vi.fn>;
  };
  let toastService: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn> };
  let router: Router;
  // Keyed rather than a single value: the component reads returnUrl, error and provider, and a
  // stub that answers every key the same way would hide a wrong-key read.
  let queryParams: Record<string, string>;

  const createComponent = () => TestBed.createComponent(Login).componentInstance;

  beforeEach(async () => {
    queryParams = {};
    accountService = {
      currentUser: vi.fn().mockReturnValue(null),
      login: vi.fn(),
      startExternalLogin: vi.fn()
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
        {
          provide: ActivatedRoute,
          useValue: {snapshot: {queryParamMap: {get: (key: string) => queryParams[key] ?? null}}}
        }
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
    queryParams['returnUrl'] = '/register';
    accountService.login.mockReturnValue(of({} as never));
    const navSpy = vi.spyOn(router, 'navigateByUrl');

    createComponent().login({invalid: false} as NgForm);

    expect(navSpy).toHaveBeenCalledWith('/');
  });

  it('returns to a normal returnUrl after login', () => {
    queryParams['returnUrl'] = '/auctions/5?pay=1';
    accountService.login.mockReturnValue(of({} as never));
    const navSpy = vi.spyOn(router, 'navigateByUrl');

    createComponent().login({invalid: false} as NgForm);

    expect(navSpy).toHaveBeenCalledWith('/auctions/5?pay=1');
  });

  it('rejects a non-local returnUrl and goes home', () => {
    queryParams['returnUrl'] = '//evil.com';
    accountService.login.mockReturnValue(of({} as never));
    const navSpy = vi.spyOn(router, 'navigateByUrl');

    createComponent().login({invalid: false} as NgForm);

    expect(navSpy).toHaveBeenCalledWith('/');
  });

  it('says nothing when the callback reported no error', () => {
    createComponent();

    expect(toastService.error).not.toHaveBeenCalled();
  });

  it('explains a refused email without needing the provider name', () => {
    queryParams['error'] = 'email_has_password';

    createComponent();

    expect(toastService.error).toHaveBeenCalledWith(
      'That email already has a password account. Log in with your password instead.');
  });

  it('names the provider the callback reported', () => {
    queryParams['error'] = 'no_email';
    queryParams['provider'] = 'Google';

    createComponent();

    expect(toastService.error).toHaveBeenCalledWith(
      'We could not sign you in because Google did not share an email address.');
  });

  it('tells the user where to verify an unverified email', () => {
    queryParams['error'] = 'email_unverified';
    queryParams['provider'] = 'Google';

    createComponent();

    expect(toastService.error).toHaveBeenCalledWith(
      'Google has not verified that email address. Verify it there, then try again.');
  });

  it('stays generic when the callback omits the provider', () => {
    queryParams['error'] = 'external_failed';

    createComponent();

    expect(toastService.error).toHaveBeenCalledWith(
      'Sign-in with your provider did not finish. Please try again.');
  });

  it('falls back for a reason code it does not know', () => {
    queryParams['error'] = 'something_new';

    createComponent();

    expect(toastService.error).toHaveBeenCalledWith('Sign-in failed. Please try again.');
  });

  it('starts external login with the sanitised returnUrl', () => {
    queryParams['returnUrl'] = '/auctions/5?pay=1';

    createComponent().continueWith('Google');

    expect(accountService.startExternalLogin).toHaveBeenCalledWith('Google', '/auctions/5?pay=1');
  });

  it('does not carry a hostile returnUrl into external login', () => {
    queryParams['returnUrl'] = '//evil.com';

    createComponent().continueWith('Google');

    expect(accountService.startExternalLogin).toHaveBeenCalledWith('Google', '/');
  });
});
