# M12: End-to-end test, Docker, deployment, README and portfolio material

## Goal
Turn the working app into a public, verifiable portfolio piece.

## Context
M01 to M11 are complete. Check current free-tier limits of the hosting services at the time you do this; do not rely on memory. As of the research for this project, some free hosts sleep idle services and some free databases expire, so verify before choosing.

## Scope
1. Playwright end-to-end test of the main flow: log in, import a CSV with good and bad rows, see the dashboard update, drill down to the grid, export. Run it in CI.
2. Production Dockerfiles for the API and the web app (multi-stage builds, non-root user where practical) and a `docker-compose.yml` that runs the full stack locally with one command.
3. Deployment to a free or inexpensive host chosen after you verify current limits. Document the choice, the free-tier limitations (cold starts, memory, database lifetime or size) and the monthly cost in `DECISIONS.md`. Configure secrets through the host's environment settings, never in the repo. Check that the API's memory use fits the host's limit.
4. A deployed demo that signs in with the documented demo credentials, plus a visible note about the cold-start delay if it applies.
5. README rewrite: what it does and why, screenshots (dashboard, grid, import result), a short architecture overview with a diagram, features list (only what is implemented and verified), tech stack with brief reasons, setup instructions that you test from a clean clone, how to run the tests, demo link and credentials, technical decisions and challenges, and a clear note that it is an independent portfolio project built with AI assistance.
6. `docs/ARCHITECTURE.md` updated to match what was actually built.
7. Produce a **portfolio kit** in `docs/portfolio/`:
   - Upwork portfolio title and a concise description (client-focused, no buzzword stuffing, no invented results).
   - The business problem and solution, my role (solo developer, AI-assisted), technologies, and implemented features.
   - The three or four most valuable technical decisions.
   - A suggested screenshot sequence.
   - Search keywords.
   - A short paragraph on which client services this project supports (dashboards, reporting tools, CSV import pipelines, Angular and .NET full-stack work).
   Do not include testimonials, revenue claims, or performance numbers that were not measured and recorded in `PROGRESS.md`.

## Out of scope
New features.

## Acceptance criteria
- The Playwright test passes locally and in CI.
- A fresh clone follows the README and runs with the documented commands (test this, and report any step that failed).
- The live demo URL works, and the cold-start behavior is documented honestly.
- No secrets, employer material or real personal data in the repository (run a quick scan and report it).
- Every number in the README appears in `PROGRESS.md` as measured.

## Verification required
Actually run the e2e test, build the images, run the stack from a clean checkout, and load the deployed URL. Report the real outcome of each, and list anything you could not verify.
