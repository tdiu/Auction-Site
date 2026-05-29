import {HttpErrorResponse, HttpRequest} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {throwError} from 'rxjs';
import {errorInterceptor} from './error-interceptor';

describe('errorInterceptor', () => {
  const request = new HttpRequest('GET', '/api/test');
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    router = {navigateByUrl: vi.fn()};

    TestBed.configureTestingModule({
      providers: [
        {provide: Router, useValue: router}
      ]
    });
  });

  function runInterceptor(error: HttpErrorResponse) {
    return TestBed.runInInjectionContext(() =>
      errorInterceptor(request, () => throwError(() => error))
    );
  }

  it('navigates to not found for 404 responses', () => {
    const error = new HttpErrorResponse({status: 404, error: {title: 'Not Found'}});

    runInterceptor(error).subscribe({error: () => undefined});

    expect(router.navigateByUrl).toHaveBeenCalledWith('/not-found');
  });

  it('navigates to server error for 500 responses with ProblemDetails state', () => {
    const problem = {title: 'Server Error', detail: 'An internal error has occurred'};
    const error = new HttpErrorResponse({status: 500, error: problem});

    runInterceptor(error).subscribe({error: () => undefined});

    expect(router.navigateByUrl).toHaveBeenCalledWith('/server-error', {
      state: {error: problem}
    });
  });

  it.each([400, 401, 409])('rethrows %i responses for component-level handling', (status) => {
    const error = new HttpErrorResponse({status, error: {detail: 'Expected action error'}});

    runInterceptor(error).subscribe({
      error: (received) => {
        expect(received).toBe(error);
      }
    });

    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
