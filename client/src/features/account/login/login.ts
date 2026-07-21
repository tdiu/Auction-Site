import {Component, inject} from '@angular/core';
import {FormsModule, NgForm} from '@angular/forms';
import {ActivatedRoute, Router, RouterLink} from '@angular/router';
import {finalize} from 'rxjs';
import {AccountService} from '../../../core/services/account-service';
import {ToastService} from '../../../core/services/toast-service';
import {getApiErrorMessage} from '../../../types/error';
import {LoginCreds} from '../../../types/user';

@Component({
  selector: 'app-login',
  imports: [FormsModule, RouterLink],
  templateUrl: './login.html',
})
export class Login {
  private accountService = inject(AccountService);
  private toast = inject(ToastService);
  private router = inject(Router);
  private route = inject(ActivatedRoute);

  protected creds = {} as LoginCreds;
  protected hasSubmitted = false;
  protected isLoggingIn = false;

  // Where to send the user after a successful login. Defaults home; callers (nav, auth guard,
  // the payment deep-link) pass ?returnUrl=... to come straight back to where they were.
  private returnUrl = this.resolveReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));

  // Never bounce back to an auth page — e.g. clicking Login from the signup form should land home,
  // not return to /register (or /login itself).
  private resolveReturnUrl(raw: string | null): string {
    if (!raw || raw.startsWith('/login') || raw.startsWith('/register')) return '/';
    return raw;
  }

  constructor() {
    // Already signed in? Nothing to do here — bounce to the destination.
    if (this.accountService.currentUser()) this.router.navigateByUrl(this.returnUrl);
  }

  login(loginForm: NgForm) {
    this.hasSubmitted = true;
    if (loginForm.invalid) return;

    this.isLoggingIn = true;
    this.accountService.login(this.creds).pipe(
      finalize(() => this.isLoggingIn = false),
    ).subscribe({
      next: () => {
        this.toast.success('Logged in successfully');
        this.router.navigateByUrl(this.returnUrl);
      },
      error: err => this.toast.error(getApiErrorMessage(err, 'Login failed')),
    });
  }
}
