import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SESSION_STORAGE_KEY } from '../../core/auth/auth.service';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  let controller: HttpTestingController;
  let router: Router;

  async function setup() {
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    const fixture = TestBed.createComponent(LoginPage);
    await fixture.whenStable();
    return fixture;
  }

  async function fillAndSubmit(fixture: Awaited<ReturnType<typeof setup>>, email: string, password: string) {
    const root = fixture.nativeElement as HTMLElement;
    const emailInput = root.querySelector<HTMLInputElement>('#email')!;
    const passwordInput = root.querySelector<HTMLInputElement>('#password')!;
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    root.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => sessionStorage.clear());

  it('shows the demo credentials', async () => {
    const fixture = await setup();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('demo@numera.example');
    expect(text).toContain('Demo-Pass-2026!');
  });

  it('signs in, stores the session and navigates to the dashboard', async () => {
    const fixture = await setup();
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

    await fillAndSubmit(fixture, 'demo@numera.example', 'Demo-Pass-2026!');
    const request = controller.expectOne('/api/auth/login');
    expect(request.request.body).toEqual({ email: 'demo@numera.example', password: 'Demo-Pass-2026!' });
    request.flush({
      accessToken: 'tok',
      tokenType: 'Bearer',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
    });

    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toContain('tok');
    expect(navigate).toHaveBeenCalledWith('/dashboard');
  });

  it('shows an error message when the credentials are rejected', async () => {
    const fixture = await setup();

    await fillAndSubmit(fixture, 'demo@numera.example', 'wrong');
    controller.expectOne('/api/auth/login').flush({}, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Incorrect email or password');
  });
});