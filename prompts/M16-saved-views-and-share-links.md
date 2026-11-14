# M16: Saved views and share links

## Goal
Users can save a dashboard view under a name and share a read-only, expiring link to it with someone who has no account.

## Context
Read `docs/SPEC.md` section 12. Filters live in the URL (M07, M09), and so does the comparison setting (M13).

## Scope
1. Saved views: name (1 to 80 characters, unique per user), the filter, the granularity and the comparison mode, stored per user. New table and migration. Endpoints to list, create, rename, update and delete them; a user only ever sees their own.
2. Dashboard: "Save view" (name prompt), a "Views" menu to open, rename or delete one, and a mark when the screen still matches the open view.
3. Share links for a saved view: an expiry (1, 7 or 30 days), and revoking.
   - The token is 32 random bytes, URL-safe. Only its SHA-256 hash is stored.
   - A view can have several links.
4. Public endpoints `/api/shared/{token}/summary`, `/timeseries` and `/breakdown`, plus the drivers endpoint from M14. They always use the view's saved filter: query filters are ignored, except granularity. An unknown, expired or revoked token returns 404 with no further detail. They are rate limited per client address.
5. Shared page `/shared/{token}`: the dashboard read-only, with the view name, the dates covered and the expiry. There is no navigation to other screens, no drill-down and no export. It has its own loading, expired and error states.
6. Settings lists the active links with their expiry and a revoke button.

## Out of scope
Editing filters on a shared page, password-protected links, email sharing, sharing order rows.

## Security
The share token is the only credential for the public endpoints. It is never logged and never stored in plain form; the lookup hashes it first and then uses an indexed lookup. Shared responses carry `Cache-Control: no-store` and `Referrer-Policy: no-referrer`.

## Tests required
- Unit tests: token generation and hashing, expiry rules, view name validation.
- Integration tests: CRUD and per-user isolation; public endpoints with valid, expired, revoked and unknown tokens; query filters cannot widen a shared view; public endpoints never return order rows; rate limiting.
- Component tests: saving, opening and deleting views; the shared page's states.

## Acceptance criteria
- A shared link shows the same numbers as the signed-in dashboard for that view (automated test).
- After revoking or expiry, the link shows the expired state and the API returns 404.
- No endpoint other than login, health and `/api/shared/*` works without a token (existing test extended).

## Verification required
Run all tests; create, open (in a private window) and revoke a link in a browser.
