import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';

export const SESSION_STORAGE_KEY = 'numera.session';

interface Session {
  readonly accessToken: string;
  readonly expiresAt: string;
}

interface LoginResponse {
  readonly accessToken: string;
  readonly tokenType: string;
  readonly expiresAt: string;
}

/**
 * Holds the JWT for the current browser tab. The token lives in sessionStorage: it survives a page refresh
 * but is dropped when the tab closes, and it is never sent to non-API URLs. See DECISIONS.md (D013).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly session = signal<Session | null>(this.readStoredSession());

  /** Whether a non-expired token is held. Evaluated against the clock on every call. */
  isSignedIn(): boolean {
    return this.token() !== null;
  }

  /** The access token, or null when signed out or expired. */
  token(): string | null {
    const session = this.session();
    if (session === null) {
      return null;
    }
    if (Date.parse(session.expiresAt) <= Date.now()) {
      this.clearSession();
      return null;
    }
    return session.accessToken;
  }

  login(email: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', { email, password }).pipe(
      tap((response) => {
        const session: Session = { accessToken: response.accessToken, expiresAt: response.expiresAt };
        this.session.set(session);
        this.writeStoredSession(session);
      }),
    );
  }

  logout(): void {
    this.clearSession();
    void this.router.navigate(['/login']);
  }

  /** Called when the API rejects the token: drop the session and return to login, remembering where we were. */
  handleUnauthorized(): void {
    this.clearSession();
    void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
  }

  private clearSession(): void {
    this.session.set(null);
    try {
      sessionStorage.removeItem(SESSION_STORAGE_KEY);
    } catch {
      // Storage unavailable (private mode, blocked): the in-memory session is already cleared.
    }
  }

  private writeStoredSession(session: Session): void {
    try {
      sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    } catch {
      // Storage unavailable: the session still works until the page is refreshed.
    }
  }

  private readStoredSession(): Session | null {
    try {
      const raw = sessionStorage.getItem(SESSION_STORAGE_KEY);
      if (raw === null) {
        return null;
      }
      const parsed: unknown = JSON.parse(raw);
      if (
        typeof parsed === 'object' &&
        parsed !== null &&
        typeof (parsed as Session).accessToken === 'string' &&
        typeof (parsed as Session).expiresAt === 'string'
      ) {
        return parsed as Session;
      }
    } catch {
      // Corrupt or unavailable storage: treat as signed out.
    }
    return null;
  }
}
