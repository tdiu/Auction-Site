import {HttpErrorResponse} from '@angular/common/http';

export type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
  stackTrace?: string;
  extensions?: Record<string, unknown>;
}

export function getApiErrorMessage(error: unknown, fallback: string): string {
  const payload = error instanceof HttpErrorResponse ? error.error : error;
  const problem = getProblemDetails(payload);

  if (problem?.errors) {
    const validationErrors = Object.values(problem.errors).flat();
    if (validationErrors.length > 0) {
      return validationErrors.join('\n');
    }
  }

  if (problem?.detail) return problem.detail;
  if (problem?.title) return problem.title;

  if (Array.isArray(payload)) {
    const messages = payload.filter((item): item is string => typeof item === 'string');
    if (messages.length > 0) return messages.join('\n');
  }

  if (typeof payload === 'string') return payload;

  return fallback;
}

function getProblemDetails(error: unknown): ProblemDetails | null {
  if (!error || typeof error !== 'object') return null;

  return error as ProblemDetails;
}
