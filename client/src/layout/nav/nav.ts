import {Component, inject, signal} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {AccountService} from '../../core/services/account-service';
import {Router, RouterLink, RouterLinkActive} from '@angular/router';
import {ToastService} from '../../core/services/toast-service';

@Component({
  selector: 'app-nav',
  imports: [FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './nav.html',
  styleUrl: './nav.css',
})
export class Nav {
  protected accountService = inject(AccountService);
  private toast = inject(ToastService);
  protected creds: any = {}
  private router = inject(Router);

  login() {
    this.accountService.login(this.creds).subscribe({
      next: results => {
        this.toast.success('Logged in successfully.');
        this.creds = {};
      },
      error: error => {
        this.toast.error(error.error);
      },
    })
  }

  logout() {
    this.accountService.logout();
    this.router.navigateByUrl('/');
  }
}
