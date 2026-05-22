import {Component, inject, input, output} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {RegisterCreds, User} from '../../../types/user';
import {AccountService} from '../../../core/services/account-service';
import {ToastService} from '../../../core/services/toast-service';

@Component({
  selector: 'app-register',
  imports: [FormsModule,],
  templateUrl: './register.html',
  styleUrl: './register.css',
})
export class Register {
  private accountService = inject(AccountService);
  private toast = inject(ToastService);
  cancelRegister = output<boolean>();
  protected creds = {} as RegisterCreds;

  register() {
    this.accountService.register(this.creds).subscribe({
      next: result => {
        console.log(result);
        this.cancel();
      },
      error: err => {
        if (Array.isArray(err)) {
          err.forEach(e => this.toast.error(e));
        } else {
          this.toast.error(err.error || 'Registration failed');
        }
      }
    })
  }

  cancel() {
    this.cancelRegister.emit(false);
  }
}
