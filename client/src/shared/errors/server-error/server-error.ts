import {Component, inject} from '@angular/core';
import {Router} from '@angular/router';
import {ProblemDetails} from '../../../types/error';

@Component({
  selector: 'app-server-error',
  imports: [],
  templateUrl: './server-error.html',
  styleUrl: './server-error.css',
})
export class ServerError {
  protected error: ProblemDetails = {
    title: 'Server Error',
    detail: 'An unexpected error occurred'
  };
  private router = inject(Router)
  protected showDetails = false;

  constructor() {
    const navigation = this.router.getCurrentNavigation();
    this.error = navigation?.extras?.state?.['error'] ?? this.error;
  }

  detailsToggle() {
    this.showDetails = !this.showDetails;
  }
}
