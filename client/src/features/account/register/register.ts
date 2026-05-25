import {Component, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {RegisterCreds} from '../../../types/user';
import {AccountService} from '../../../core/services/account-service';
import {ToastService} from '../../../core/services/toast-service';
import {Router} from '@angular/router';

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

  register() {
    this.accountService.register(this.creds).subscribe({
      next: result => {
        this.toast.success('Registration successful');
        this.router.navigate(['/']);
      },
      error: err => {
        if (Array.isArray(err)) {
          err.forEach(e => this.toast.error(e));
        } else {
          this.toast.error(err?.error || 'Registration failed');
        }
      }
    })
  }

  cancel() {
    this.router.navigate(['/']);
  }
}
