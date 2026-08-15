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

  has(privilege: string): boolean {
    return this.privileges().has(privilege);
  }

  login(username: string, password: string): Observable<LoginResult> {
    return this.http.post<LoginResult>(`${environment.apiBase}/auth/login`, { username, password }).pipe(
      tap((result) => {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(result));
        this._session.set(result);
      }),
    );
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
