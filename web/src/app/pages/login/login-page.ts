import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

/** Documented demo credentials for the fictional dataset. Not real secrets. */
const DEMO_EMAIL = 'demo@numera.example';
const DEMO_PASSWORD = 'Demo-Pass-2026!';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule],
  templateUrl: './login-page.html',
  styleUrl: './login-page.css',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly demoEmail = DEMO_EMAIL;
  protected readonly demoPassword = DEMO_PASSWORD;

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  constructor() {
    if (this.auth.isSignedIn()) {
      void this.router.navigateByUrl('/dashboard');
    }
  }

  protected useDemoCredentials(): void {
    this.email.set(DEMO_EMAIL);
    this.password.set(DEMO_PASSWORD);
    this.errorMessage.set(null);
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    if (this.email().trim() === '' || this.password() === '') {
      this.errorMessage.set('Enter your email and password.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set(null);
    this.auth.login(this.email().trim(), this.password()).subscribe({
      next: () => {
        this.submitting.set(false);
        void this.router.navigateByUrl(this.safeReturnUrl());
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        this.errorMessage.set(this.messageFor(error));
      },
    });
  }

  /** Only same-origin absolute paths are honoured, so a crafted link cannot redirect off-site. */
  private safeReturnUrl(): string {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    const isLocalPath = returnUrl !== null && returnUrl.startsWith('/') && !returnUrl.startsWith('//');
    return isLocalPath && !returnUrl.startsWith('/login') ? returnUrl : '/dashboard';
  }

  private messageFor(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      switch (error.status) {
        case 400:
          return 'Enter your email and password.';
        case 401:
          return 'Incorrect email or password.';
        case 429:
          return 'Too many attempts. Wait a minute and try again.';
      }
    }
    return 'Could not reach the server. Check your connection and try again.';
  }
}
