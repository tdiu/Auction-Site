import {ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {of} from 'rxjs';
import {AccountService} from '../../core/services/account-service';
import {MessageService} from '../../core/services/message-service';
import {Nav} from './nav';

describe('Nav', () => {
  let fixture: ComponentFixture<Nav>;
  let component: Nav;
  let accountService: { currentUser: ReturnType<typeof vi.fn>; logout: ReturnType<typeof vi.fn> };
  let messageService: {
    refreshUnread: ReturnType<typeof vi.fn>;
    unreadCount: { set: ReturnType<typeof vi.fn> };
    hasUnread: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    accountService = {
      currentUser: vi.fn().mockReturnValue(null),
      logout: vi.fn().mockReturnValue(of(undefined))
    };
    messageService = {
      refreshUnread: vi.fn(),
      unreadCount: {set: vi.fn()},
      hasUnread: vi.fn().mockReturnValue(false)
    };

    await TestBed.configureTestingModule({
      imports: [Nav],
      providers: [
        {provide: AccountService, useValue: accountService},
        {provide: MessageService, useValue: messageService},
        provideRouter([])
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(Nav);
    component = fixture.componentInstance;
  });

  it('navigates home on logout', () => {
    const router = TestBed.inject(Router);
    const navSpy = vi.spyOn(router, 'navigateByUrl');

    component.logout();

    expect(navSpy).toHaveBeenCalledWith('/');
  });
});
