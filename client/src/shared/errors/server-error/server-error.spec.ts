import {ComponentFixture, TestBed} from '@angular/core/testing';
import {Router} from '@angular/router';
import {ServerError} from './server-error';

describe('ServerError', () => {
  function createComponent(errorState?: unknown): ComponentFixture<ServerError> {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ServerError],
      providers: [
        {
          provide: Router,
          useValue: {
            getCurrentNavigation: () => errorState ? {extras: {state: {error: errorState}}} : null
          }
        }
      ]
    });

    const fixture = TestBed.createComponent(ServerError);
    fixture.detectChanges();
    return fixture;
  }

  it('displays ProblemDetails detail from navigation state', () => {
    const fixture = createComponent({
      title: 'Server Error',
      detail: 'Database unavailable',
      stackTrace: 'stack trace here'
    });

    expect(fixture.nativeElement.textContent).toContain('Database unavailable');
    expect(fixture.nativeElement.textContent).toContain('Details');
  });

  it('uses safe default text without navigation state', () => {
    const fixture = createComponent();

    expect(fixture.nativeElement.textContent).toContain('An unexpected error occurred');
  });

  it('shows stack trace after details toggle', () => {
    const fixture = createComponent({
      title: 'Server Error',
      detail: 'Database unavailable',
      stackTrace: 'stack trace here'
    });

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    button.click();
    fixture.autoDetectChanges();

    expect(fixture.nativeElement.textContent).toContain('stack trace here');
  });
});
