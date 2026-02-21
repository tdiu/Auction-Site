import { Component, signal, inject, OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { Nav } from "../layout/nav/nav";

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Nav],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  private http = inject(HttpClient);
  protected readonly title = "GWAuction"
  protected users = signal<any>('[]');

  ngOnInit() {
    this.http.get('https://localhost:5001/api/Users').subscribe({
      next: response => this.users.set(response),
      error: error => console.log(error),
      complete: () => console.log('Completed')
    })
  }
}
