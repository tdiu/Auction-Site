import {Component, signal, Input, inject} from '@angular/core';
import {Register} from '../account/register/register';
import {User} from '../../types/user';
import {AccountService} from '../../core/services/account-service';
import {RouterLink} from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [
    Register,
    RouterLink
  ],
  templateUrl: './home.html',
  styleUrl: './home.css',
})
export class Home {
  protected registerMode = signal(false);
  protected accountService = inject(AccountService);

  showRegister(value: boolean) {
    this.registerMode.set(value);
  }
}
