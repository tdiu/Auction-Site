import {Component, effect, inject} from '@angular/core';
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

  constructor() {
    // Refresh the unread indicator whenever a user logs in; clear it on logout.
    effect(() => {
      if (this.accountService.currentUser()) {
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
