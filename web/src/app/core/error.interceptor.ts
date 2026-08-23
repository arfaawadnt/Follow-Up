import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from './toast.service';

/**
 * Surfaces failed mutations as toasts so a button press can never fail silently. Reads the API's RFC 7807
 * Problem Details (detail/title) for a meaningful message. Scoped to mutating requests (POST/PUT/PATCH/DELETE)
 * so background GET polls (notifications, real-time refreshes) never spam the user. 401 is left to the auth
 * interceptor (redirect to login). The error is always re-thrown so component-level handlers still run.
 */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toasts = inject(ToastService);
  const isMutation = req.method !== 'GET' && req.method !== 'HEAD';

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (isMutation && error.status !== 401) toasts.error(messageFor(error));
      return throwError(() => error);
    }),
  );
};

function messageFor(error: HttpErrorResponse): string {
  if (error.status === 0) return 'Cannot reach the server. Check your connection.';
  const problem = error.error;
  if (problem && typeof problem === 'object') {
    const detail = (problem as { detail?: string }).detail;
    const title = (problem as { title?: string }).title;
    if (detail) return detail;
    if (title) return title;
  }
  return error.message || 'Something went wrong.';
}
