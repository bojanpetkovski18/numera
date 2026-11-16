# M18: Release 2.0

## Goal
Version 2 is tested end to end, documented for clients and released as `v2.0.0`.

## Context
Read `docs/SPEC.md` section 12. M13 to M16 (and M17 if confirmed) are merged into `dev`.

## Scope
1. Extend the Playwright test: compare with the previous period and check a KPI change against the API, open a driver's rows, import the mapped sample file, save a view, open its share link in a new browser context, revoke it.
2. Run the accessibility and layout checks at 375, 768 and 1280 px on every new screen. Add them to the committed e2e suite if they run in CI in reasonable time.
3. README: the version 2 features in "What it does" and the guided tour; new screenshots; nothing claimed that was not measured.
4. `docs/ARCHITECTURE.md` and `docs/portfolio/README.md` updated; the commands section of `CLAUDE.md` updated with the new endpoints.
5. Refresh the demo video frames and captions for the new features.
6. Merge `dev` into `main`, tag `v2.0.0`, and publish a GitHub release with a short changelog. Confirm with the owner before pushing the tag.

## Out of scope
New features.

## Acceptance criteria
- CI green on `dev` and `main`, including the e2e job.
- The README tour works step by step on a fresh `docker compose up --build`.
- The release notes list only shipped features.

## Verification required
A fresh-clone run, the full test suite, the e2e test, and the live demo if it is deployed.
