import {Component, inject} from '@angular/core';
import {FormsModule, NgForm} from '@angular/forms';
import {RegisterCreds} from '../../../types/user';
import {AccountService} from '../../../core/services/account-service';
import {ToastService} from '../../../core/services/toast-service';
import {Router} from '@angular/router';
import {HttpErrorResponse} from '@angular/common/http';
import {ProblemDetails} from '../../../types/error';

type RegisterField = keyof RegisterCreds | 'form';
type RegisterFieldErrors = Partial<Record<RegisterField, string[]>>;

@Component({
  selector: 'app-register',
  imports: [FormsModule,],
  templateUrl: './register.html',
  styleUrl: './register.css',
})
export class Register {
  private accountService = inject(AccountService);
  private toast = inject(ToastService);
  private router = inject(Router);
  
  protected creds = {} as RegisterCreds;
  protected fieldErrors: RegisterFieldErrors = {};
  protected hasSubmitted = false;
  protected suppressClearedPasswordError = false;

  selectedMonth = '';
  selectedDay = '';
  selectedYear = '';

  months = [
    { name: 'January', value: '01' },
    { name: 'February', value: '02' },
    { name: 'March', value: '03' },
    { name: 'April', value: '04' },
    { name: 'May', value: '05' },
    { name: 'June', value: '06' },
    { name: 'July', value: '07' },
    { name: 'August', value: '08' },
    { name: 'September', value: '09' },
    { name: 'October', value: '10' },
    { name: 'November', value: '11' },
    { name: 'December', value: '12' }
  ];

  days = Array.from({ length: 31 }, (_, i) => {
    const d = i + 1;
    return d < 10 ? `0${d}` : `${d}`;
  });

  years = Array.from({ length: 100 }, (_, i) => (2026 - i).toString());

  updateDateOfBirth() {
    if (this.selectedYear && this.selectedMonth && this.selectedDay) {
      this.creds.dateOfBirth = `${this.selectedYear}-${this.selectedMonth}-${this.selectedDay}`;
    } else {
      this.creds.dateOfBirth = '';
    }
  }

  register(registerForm: NgForm) {
    this.hasSubmitted = true;
    this.suppressClearedPasswordError = false;
    this.fieldErrors = {};

    if (registerForm.invalid) return;

    this.accountService.register(this.creds).subscribe({
      next: result => {
        this.toast.success('Registration successful');
        this.router.navigate(['/']);
      },
      error: err => {
        this.fieldErrors = this.mapRegistrationErrors(err);
        this.clearPassword(registerForm);
      }
    })
  }

  protected clearFieldError(field: keyof RegisterCreds) {
    if (!this.fieldErrors[field]) return;

    const {[field]: _removed, ...remainingErrors} = this.fieldErrors;
    this.fieldErrors = remainingErrors;
  }

  protected onPasswordChange() {
    this.suppressClearedPasswordError = false;
    this.clearFieldError('password');
  }

  protected getFieldErrors(field: RegisterField): string[] {
    return this.fieldErrors[field] ?? [];
  }

  protected hasFieldErrors(field: RegisterField): boolean {
    return this.getFieldErrors(field).length > 0;
  }

  cancel() {
    this.router.navigate(['/']);
  }

  private mapRegistrationErrors(error: unknown): RegisterFieldErrors {
    const payload = error instanceof HttpErrorResponse ? error.error : error;
    const problem = this.getProblemDetails(payload);
    const fieldErrors: RegisterFieldErrors = {};

    if (problem?.errors) {
      Object.entries(problem.errors).forEach(([field, messages]) => {
        this.addFieldErrors(fieldErrors, this.normalizeFieldName(field), messages);
      });
    } else {
      const messages = this.getErrorMessages(payload, problem);
      messages.forEach(message => {
        this.addFieldErrors(fieldErrors, this.getFieldForMessage(message), [message]);
      });
    }

    if (Object.keys(fieldErrors).length === 0) {
      fieldErrors.form = ['Registration failed'];
    }

    return fieldErrors;
  }

  private getProblemDetails(error: unknown): ProblemDetails | null {
    if (!error || typeof error !== 'object') return null;

    return error as ProblemDetails;
  }

  private getErrorMessages(payload: unknown, problem: ProblemDetails | null): string[] {
    if (problem?.detail) {
      return problem.detail
        .split(';')
        .map(message => message.trim())
        .filter(Boolean);
    }

    if (Array.isArray(payload)) {
      return payload.filter((message): message is string => typeof message === 'string');
    }

    if (typeof payload === 'string') return [payload];

    return [];
  }

  private normalizeFieldName(field: string): RegisterField {
    const normalized = field.toLowerCase();

    if (normalized === 'email') return 'email';
    if (normalized === 'displayname' || normalized === 'username') return 'displayName';
    if (normalized === 'password') return 'password';
    if (normalized === 'dateofbirth') return 'dateOfBirth';

    return 'form';
  }

  private getFieldForMessage(message: string): RegisterField {
    const normalized = message.toLowerCase();

    if (
      normalized.includes('password') ||
      normalized.includes('digit') ||
      normalized.includes('uppercase')
    ) {
      return 'password';
    }

    if (normalized.includes('email')) return 'email';
    if (normalized.includes('display name') || normalized.includes('username')) return 'displayName';
    if (normalized.includes('date of birth')) return 'dateOfBirth';

    return 'form';
  }

  private addFieldErrors(
    fieldErrors: RegisterFieldErrors,
    field: RegisterField,
    messages: string[]
  ) {
    const existingMessages = fieldErrors[field] ?? [];
    fieldErrors[field] = [...existingMessages, ...messages];
  }

  private clearPassword(registerForm: NgForm) {
    this.creds = {...this.creds, password: ''};
    registerForm.controls['password']?.reset('');
    this.suppressClearedPasswordError = true;
  }
}
