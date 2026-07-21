import {Component, computed, effect, inject} from '@angular/core';
import {AccountService} from '../../core/services/account-service';
import {Router, RouterLink, RouterLinkActive} from '@angular/router';
import {MessageService} from '../../core/services/message-service';

@Component({
  selector: 'app-nav',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './nav.html',
  styleUrl: './nav.css',
})
export class Nav {
  protected accountService = inject(AccountService);
  protected messageService = inject(MessageService);
  protected router = inject(Router);

  // Track only the id, so a new User object with the same id (e.g. a token refresh) doesn't
  // re-fire the effect. Computed changes only when the id itself changes.
  private userId = computed(() => this.accountService.currentUser()?.id ?? null);

  constructor() {
    // Refresh the unread indicator on login (id appears); clear it on logout (id goes null).
    effect(() => {
      if (this.userId()) {
        this.messageService.refreshUnread();
      } else {
        this.messageService.unreadCount.set(0);
      }
    });
  }

  logout() {
    this.accountService.logout().subscribe({
      next: () => this.router.navigateByUrl('/'),
      error: () => this.router.navigateByUrl('/'),
    })
  }
}
