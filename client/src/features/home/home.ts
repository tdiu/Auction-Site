import {Component, signal, Input, inject} from '@angular/core';
import {Register} from '../account/register/register';
import {User} from '../../types/user';
import {AccountService} from '../../core/services/account-service';
import {RouterLink} from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [
    RouterLink
  ],
  templateUrl: './home.html',
  styleUrl: './home.css',
})
export class Home {
  protected accountService = inject(AccountService);
}
