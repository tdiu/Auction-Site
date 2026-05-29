import {HttpErrorResponse} from '@angular/common/http';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {throwError} from 'rxjs';
import {AccountService} from '../../core/services/account-service';
import {ToastService} from '../../core/services/toast-service';
import {Nav} from './nav';

describe('Nav', () => {
  let fixture: ComponentFixture<Nav>;
  let component: Nav;
  let accountService: { currentUser: ReturnType<typeof vi.fn>; login: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let toastService: { error: ReturnType<typeof vi.fn>; success: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    accountService = {
      currentUser: vi.fn().mockReturnValue(null),
      login: vi.fn(),
      logout: vi.fn()
    };
    toastService = {
      error: vi.fn(),
      success: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [Nav],
      providers: [
        {provide: AccountService, useValue: accountService},
        {provide: ToastService, useValue: toastService},
        provideRouter([])
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(Nav);
    component = fixture.componentInstance;
  });

  it('shows ProblemDetails detail when login fails', () => {
    accountService.login.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 401,
      error: {title: 'Unauthorized', detail: 'Invalid Credentials'}
    })));

    component.login();

    expect(toastService.error).toHaveBeenCalledWith('Invalid Credentials');
  });
});
