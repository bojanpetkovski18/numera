import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { SESSION_STORAGE_KEY } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let router: Router;

  function signIn(): void {
    sessionStorage.setItem(
      SESSION_STORAGE_KEY,
      JSON.stringify({ accessToken: 'abc.def.ghi', expiresAt: new Date(Date.now() + 60_000).toISOString() }),
    );
  }

  function configure(): void {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  }

  beforeEach(() => sessionStorage.clear());
  afterEach(() => {
    controller.verify();
    sessionStorage.clear();
  });

  it('attaches the bearer token to API requests', () => {
    signIn();
    configure();

    http.get('/api/orders').subscribe();

    const request = controller.expectOne('/api/orders');
    expect(request.request.headers.get('Authorization')).toBe('Bearer abc.def.ghi');
    request.flush([]);
  });

  it('does not attach the token to non-API requests', () => {
    signIn();
    configure();

    http.get('https://example.test/data').subscribe();

    const request = controller.expectOne('https://example.test/data');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('sends no Authorization header when signed out', () => {
    configure();

    http.get('/api/orders').subscribe();

    const request = controller.expectOne('/api/orders');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush([]);
  });

  it('clears the session and redirects to login on a 401 from the API', () => {
    signIn();
    configure();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    http.get('/api/orders').subscribe({ error: () => undefined });
    controller.expectOne('/api/orders').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(sessionStorage.getItem(SESSION_STORAGE_KEY)).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], expect.anything());
  });

  it('does not redirect when the login request itself returns 401', () => {
    configure();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    http.post('/api/auth/login', {}).subscribe({ error: () => undefined });
    controller.expectOne('/api/auth/login').flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });
});