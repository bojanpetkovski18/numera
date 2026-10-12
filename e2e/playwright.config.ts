import { defineConfig, devices } from '@playwright/test';

/**
 * Runs against an already running stack: `docker compose up --build` serves the app on http://localhost:8080.
 * E2E_BASE_URL points elsewhere (for example the dev server on 4200); E2E_BROWSER_CHANNEL=msedge uses an
 * installed Edge instead of Playwright's Chromium.
 */
export default defineConfig({
  testDir: './tests',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env['CI'],
  reporter: process.env['CI'] ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:8080',
    acceptDownloads: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], channel: process.env['E2E_BROWSER_CHANNEL'] || undefined },
    },
  ],
});
