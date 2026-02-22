import { Component, signal, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { Nav } from "../layout/nav/nav";
import {AccountService} from '../core/services/account-service';
import {Home} from '../features/home/home';
import {User} from '../types/user';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Nav, Home],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  private accountService = inject(AccountService);
  private http = inject(HttpClient);
  protected readonly title = "GWAuction"
  protected users = signal<User[]>([]);

  ngOnInit() {
    this.http.get<User[]>('https://localhost:5001/api/Users').subscribe({
      next: response => this.users.set(response),
      error: error => console.log(error),
      complete: () => console.log('Completed')
    })
    this.setCurrentUser();
  }

  setCurrentUser() {
    const userString = localStorage.getItem('user');
    if (!userString) return;
    const user = JSON.parse(userString);
    this.accountService.currentUser.set(user);
  }
}
