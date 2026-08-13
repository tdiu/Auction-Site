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

  // Only accept a local path: one leading slash, but not '//host' or '/\host' (which browsers can
  // treat as protocol-relative and navigate off-origin).
  // clicking Login from the signup form should land home, not return to /register (or /login itself).
  private resolveReturnUrl(raw: string | null): string {
    if (!raw || !raw.startsWith('/') || raw.startsWith('//') || raw.startsWith('/\\')) return '/';
    const path = raw.split(/[?#]/, 1)[0];
    if (path === '/login' || path === '/register') return '/';
    return raw;
  }

  // The callback can only hand back a reason code, so the copy lives here. Each message names the
  // other way in: under the no-linking rule a refused user has no self-service recovery, and the
  // wording is the whole mitigation. The provider is interpolated rather than baked into the
  // strings, so adding one does not silently make these sentences lie.
  private static readonly externalErrors: Record<string, (provider: string) => string> = {
    email_has_password: () =>
      'That email already has a password account. Log in with your password instead.',
    no_email: provider =>
      `We could not sign you in because ${provider} did not share an email address.`,
    email_unverified: provider =>
      `${provider} has not verified that email address. Verify it there, then try again.`,
    external_failed: provider =>
      `Sign-in with ${provider} did not finish. Please try again.`,
  };

  constructor() {
    this.showExternalError();

    // Go to destination if already logged in
    if (this.accountService.currentUser()) this.router.navigateByUrl(this.returnUrl);
  }

  // ?error=<code>&provider=<name>, set by the API callback when it redirects back here.
  private showExternalError() {
    const params = this.route.snapshot.queryParamMap;
    const code = params.get('error');
    if (!code) return;

    const provider = params.get('provider') ?? 'your provider';
    this.toast.error(Login.externalErrors[code]?.(provider) ?? 'Sign-in failed. Please try again.');
  }

  // returnUrl is already sanitised by resolveReturnUrl, so the deep-link cases (auth guard,
  // payment) survive the round trip. The server re-validates it regardless.
  continueWith(provider: string) {
    this.accountService.startExternalLogin(provider, this.returnUrl);
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
