# M03: Authentication (JWT, demo user)

## Goal
Protect the API and sign in from the Angular app using a seeded demo user.

## Context
Read `CLAUDE.md` security rules. M01 and M02 are complete.

## Scope
1. Seed one demo user (email `demo@numera.example`, a documented demo password). Store only a password hash (use ASP.NET Core's `PasswordHasher` or an equivalent standard approach).
2. `POST /api/auth/login`: validates credentials, returns a short-lived JWT access token. Signing key, issuer, audience and lifetime come from configuration, never hard-coded. Keep it simple; a refresh-token flow is out of scope unless it adds little code, and if you add it, log the decision.
3. Require authentication on all endpoints except `/health` and `/api/auth/login`. Return 401 for missing or invalid tokens, with problem-details bodies.
4. Basic brute-force protection: rate limiting on the login endpoint.
5. Angular: a Login page (shows the demo credentials clearly), an auth service, an HTTP interceptor that attaches the token and handles 401 by redirecting to login, and a route guard on all pages except Login. Store the token in a way you can justify; record the trade-off in `DECISIONS.md`.
6. Logout in the top bar.

## Out of scope
Roles and permissions, registration, password reset.

## Tests required
- Unit: password verification, token generation contains the expected claims and expiry.
- Integration: login success, login with wrong password returns 401, a protected endpoint returns 401 without a token and 200 with a valid one, `/health` stays anonymous.
- Angular: guard redirects unauthenticated users; interceptor attaches the token.

## Acceptance criteria
- Unauthenticated requests to protected endpoints return 401.
- Logging in through the UI reaches the dashboard placeholder; refreshing the page keeps the session until expiry; logout returns to Login.
- No secret is committed; `.env.example` documents the needed settings.

## Verification required
Run the tests and exercise the login flow in a browser or with HTTP requests, and report what you actually saw.
