# M11: Demo reset, consistent states, responsive polish

## Goal
Make it safe to share publicly and pleasant to use on every device.

## Context
All feature milestones M01 to M10 are complete.

## Scope
1. `POST /api/demo/reset`: wipes imported and seeded order data and reseeds the deterministic dataset. Requires authentication, is rate limited, and is protected against concurrent runs. A Settings screen has a "Reset demo data" button with a confirmation dialog, progress and result states.
2. Audit every screen for loading, empty, error and success states and fix gaps. Make wording consistent and human.
3. Responsive pass at 375 px, 768 px and desktop: navigation collapses sensibly, tables and charts adapt, no horizontal page scroll, touch targets are large enough.
4. Accessibility pass: keyboard navigation through all flows, visible focus, labels on inputs, sufficient contrast, sensible page titles, `aria` where needed. Run an automated accessibility check (for example axe) and fix what it finds; report what remains.
5. A global error boundary/handler in Angular and a friendly "not found" page.
6. Review the whole codebase for leftover TODOs, dead code, unused dependencies and console logs. Remove them.

## Out of scope
New features. Deployment (M12).

## Tests required
- Integration: reset restores the exact seeded counts; unauthenticated reset returns 401; concurrent reset is handled safely.
- Angular: Settings flow tests (confirm, cancel, success, failure).

## Acceptance criteria
- Reset returns the demo to the original seeded state and the dashboard shows the original numbers again.
- The app is usable at 375 px width on every screen.
- The automated accessibility check reports no serious or critical issues on the main screens, or the remaining ones are listed honestly.

## Verification required
Run all tests, run the accessibility check, and walk through every screen at three widths in a real browser. Report real results and anything left unfixed.
