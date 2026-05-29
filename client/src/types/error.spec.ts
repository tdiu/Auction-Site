import {HttpErrorResponse} from '@angular/common/http';
import {getApiErrorMessage} from './error';

describe('getApiErrorMessage', () => {
  it('returns flattened validation messages from ProblemDetails errors', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: {
        title: 'Validation Failed',
        detail: 'One or more validation errors occurred',
        errors: {
          itemName: ['Item name is required'],
          startingPrice: ['Starting price must be greater than 0']
        }
      }
    });

    expect(getApiErrorMessage(error, 'Fallback')).toBe(
      'Item name is required\nStarting price must be greater than 0'
    );
  });

  it('prefers detail over title', () => {
    const error = new HttpErrorResponse({
      status: 409,
      error: {
        title: 'Conflict',
        detail: 'Bid is too low'
      }
    });

    expect(getApiErrorMessage(error, 'Fallback')).toBe('Bid is too low');
  });

  it('uses title when detail is missing', () => {
    const error = new HttpErrorResponse({
      status: 401,
      error: {
        title: 'Unauthorized'
      }
    });

    expect(getApiErrorMessage(error, 'Fallback')).toBe('Unauthorized');
  });

  it('handles string arrays', () => {
    expect(getApiErrorMessage(['First error', 'Second error'], 'Fallback')).toBe(
      'First error\nSecond error'
    );
  });

  it('handles raw strings', () => {
    expect(getApiErrorMessage('Plain error', 'Fallback')).toBe('Plain error');
  });

  it('returns fallback for empty error payloads', () => {
    expect(getApiErrorMessage({}, 'Fallback')).toBe('Fallback');
    expect(getApiErrorMessage(null, 'Fallback')).toBe('Fallback');
  });
});
