import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { LoginResult } from './models';

const STORAGE_KEY = 'followup.session';

/**
 * Auth state as signals. The token + resolved session are persisted so a refresh keeps the user signed in;
 * privileges are the server's expanded set (the backend re-checks on every call — the client only hides UI).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly _session = signal<LoginResult | null>(this.restore());

  readonly session = this._session.asReadonly();
  readonly isAuthenticated = computed(() => this._session() !== null);
  readonly username = computed(() => this._session()?.username ?? '');
  readonly roleName = computed(() => this._session()?.roleName ?? '');
  readonly privileges = computed(() => new Set(this._session()?.privileges ?? []));

  constructor(private readonly http: HttpClient) {}

  get token(): string | null {
    return this._session()?.token ?? null;
  }

  /** Client-side mirror of the server's coarse → fine implications for privileges added after a session was issued (the
   *  server re-reads the role on every call, so a session signed in before a release lacks the new leaf in its cached
   *  list although the API already allows it). Keep in step with Privileges.Expansions. */
  private static readonly IMPLIES: Record<string, string[]> = { ManageUsers: ['ViewAuditTrail', 'ViewRegistrationChanges'] };

  has(privilege: string): boolean {
    const p = this.privileges();
    if (p.has(privilege)) return true;
    return Object.entries(AuthService.IMPLIES).some(([coarse, leaves]) => leaves.includes(privilege) && p.has(coarse));
  }

  login(username: string, password: string): Observable<LoginResult> {
    return this.http.post<LoginResult>(`${environment.apiBase}/auth/login`, { username, password }).pipe(
      tap((result) => {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(result));
        this._session.set(result);
      }),
    );
  }

  changePassword(oldPassword: string, newPassword: string): Observable<unknown> {
    return this.http.post(`${environment.apiBase}/user/change-password`, { oldPassword, newPassword });
  }

  logout(): void {
    // Best-effort server revoke; clear local state regardless.
    this.http.post(`${environment.apiBase}/auth/logout`, {}).subscribe({ error: () => {} });
    localStorage.removeItem(STORAGE_KEY);
    this._session.set(null);
  }

  clearLocal(): void {
    localStorage.removeItem(STORAGE_KEY);
    this._session.set(null);
  }

  private restore(): LoginResult | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as LoginResult;
      return new Date(session.expiresAt) > new Date() ? session : null;
    } catch {
      return null;
    }
  }
}
