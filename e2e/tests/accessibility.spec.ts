import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

/**
 * Accessibility and layout at phone, tablet and desktop widths: axe-core (WCAG 2.0 to 2.2 A and AA plus best
 * practices) on every main screen and the v2 states, and no sideways scrolling. Read-only: nothing is saved.
 */
const AXE = readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');
const MAPPED_SAMPLE = join(__dirname, '..', '..', 'web', 'public', 'sample-orders-mapped.csv');

async function check(page: Page, screen: string): Promise<void> {
  // Evaluated through the browser's DevTools protocol, so the page's Content-Security-Policy does not block it.
  await page.evaluate(AXE);
  const violations = await page.evaluate(async () => {
    const result = await (window as unknown as { axe: { run: (c: Document, o: object) => Promise<{ violations: { id: string; nodes: { target: string[] }[] }[] }> } })
      .axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'] });
    return result.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(' | ')}`);
  });
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(violations, `${screen}: axe violations`).toEqual([]);
  expect(overflow, `${screen}: sideways overflow in px`).toBe(0);
}

for (const width of [375, 768, 1280]) {
  test(`screens are accessible and fit at ${width} px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.emulateMedia({ reducedMotion: 'reduce' });

    await page.goto('/login');
    await check(page, 'login');
    await page.getByRole('button', { name: 'Fill in demo credentials' }).click();
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);

    await page.goto('/dashboard?from=2026-07-01&to=2026-09-30&compare=previous');
    await expect(page.locator('app-what-changed .driver').first()).toBeVisible();
    await expect(page.locator('app-revenue-chart canvas')).toBeVisible();
    await check(page, 'dashboard with a comparison');

    await page.getByRole('button', { name: /^Saved views/ }).click();
    await expect(page.locator('#views-list')).toBeVisible();
    await check(page, 'saved views');

    await page.goto('/orders?region=Europe&status=completed');
    await expect(page.locator('table.orders tbody tr').first()).toBeVisible();
    await check(page, 'orders');

    await page.goto('/import');
    await page.locator('#file-input').setInputFiles(MAPPED_SAMPLE);
    await expect(page.locator('app-column-mapping')).toBeVisible();
    await check(page, 'import mapping step');
    await page.locator('app-column-mapping').getByRole('button', { name: 'Cancel' }).click();

    await page.goto('/settings');
    await expect(page.locator('app-shared-links')).toBeVisible();
    await check(page, 'settings');

    await page.goto(`/shared/${'x'.repeat(43)}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('This link no longer works');
    await check(page, 'expired share link');
  });
}
