import { join } from 'node:path';
import { expect, test, type Page } from '@playwright/test';

/**
 * The version 2 flow: compare periods (and check a KPI change against the API), open a driver's rows, import a file
 * with other column names through the mapping step, save a view, open its share link without signing in, revoke it.
 * Ends with the demo reset, so the shared demo is left as it was found. Names are unique to the run.
 */
const run = Date.now().toString(36).toUpperCase();
const viewName = `E2E view ${run}`;
const Q3 = '/dashboard?from=2026-07-01&to=2026-09-30&compare=previous';
const MAPPED_SAMPLE = join(__dirname, '..', '..', 'web', 'public', 'sample-orders-mapped.csv');

function kpi(page: Page, label: string) {
  return page.locator('app-kpi-cards article').filter({ has: page.getByRole('heading', { name: label, exact: true }) });
}

/** What the signed-in API returns, called with the page's own session. */
async function api<T>(page: Page, path: string): Promise<T> {
  return page.evaluate(async (url) => {
    const session = JSON.parse(sessionStorage.getItem('numera.session') ?? '{}') as { accessToken?: string };
    const response = await fetch(url, { headers: { Authorization: `Bearer ${session.accessToken}` } });
    return response.json();
  }, path) as Promise<T>;
}

test('compare, explain, import with a mapping, share a view, revoke it', async ({ page, browser }) => {
  const consoleErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => consoleErrors.push(error.message));

  await test.step('sign in', async () => {
    await page.goto('/login');
    await page.getByRole('button', { name: 'Fill in demo credentials' }).click();
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
  });

  await test.step('compare Q3 2026 with the previous period; the KPI change is the API\'s', async () => {
    await page.goto(Q3);
    await expect(page.locator('#f-compare')).toHaveValue('previous');
    const change = kpi(page, 'Revenue').locator('.change');
    await expect(change).toContainText('vs previous period');

    const summary = await api<{ changes: { revenue: number } }>(page, '/api/metrics/summary?from=2026-07-01&to=2026-09-30&compare=previous');
    const percent = new Intl.NumberFormat('en-US', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1, signDisplay: 'exceptZero' })
      .format(Math.round(summary.changes.revenue * 1000) / 1000);
    await expect(change).toContainText(percent);
    await expect(page.locator('app-kpi-cards .compared')).toHaveText('Compared with Mar 31, 2026 to Jun 30, 2026 (previous period).');
  });

  await test.step('"What changed?" opens the rows behind the biggest driver', async () => {
    const panel = page.locator('app-what-changed');
    await expect(panel.locator('.summary')).toContainText(/^Revenue (rose|fell) /);
    const first = panel.locator('.driver').first();
    const region = (await first.locator('.name').innerText()).split('\n')[0].trim();
    await first.locator('a.name').click();

    await expect(page).toHaveURL(/\/orders\?/);
    const url = new URL(page.url());
    expect([url.searchParams.get('from'), url.searchParams.get('to'), url.searchParams.get('region'), url.searchParams.get('status')]).toEqual([
      '2026-07-01', '2026-09-30', region, 'completed',
    ]);
    await expect(page.locator('.summary')).toContainText('order lines');
  });

  await test.step('import a file with other column names through the mapping step', async () => {
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Import' }).click();
    await page.locator('#file-input').setInputFiles(MAPPED_SAMPLE);

    const mapping = page.locator('app-column-mapping');
    await expect(mapping.getByRole('heading', { name: 'Match your columns' })).toBeVisible();
    await expect(page.locator('#map-quantity')).toHaveValue('Qty');
    await expect(page.locator('#map-date-warning')).toBeVisible(); // 01/03/2025 reads both ways
    await page.locator('#map-date-format').selectOption('dd/MM/yyyy');
    await expect(page.locator('#map-date-warning')).toBeHidden();
    await mapping.getByRole('button', { name: 'Import with this mapping' }).click();

    const result = page.getByRole('region', { name: 'Import result' });
    await expect(result.locator('.counts')).toContainText(/Rows read\s*5/);
    await expect(result).toContainText("Column 'Notes' was not mapped and was ignored.");
  });

  let shareUrl = '';
  await test.step('save the compared view and create a share link', async () => {
    await page.goto(Q3);
    await expect(kpi(page, 'Revenue').locator('.change')).toBeVisible();
    await page.getByRole('button', { name: 'Save view' }).click();
    await page.locator('#view-name').fill(viewName);
    await page.locator('#views-save').getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.locator('app-views-bar .current')).toHaveText(`Saved view: ${viewName}`);

    await page.getByRole('button', { name: /^Saved views/ }).click();
    await page.getByRole('button', { name: `Share ${viewName}` }).click();
    await page.getByRole('button', { name: 'Create link' }).click();
    const field = page.locator('input[id^="share-url-"]');
    await expect(field).toHaveValue(/\/shared\/[A-Za-z0-9_-]{43}$/);
    shareUrl = await field.inputValue();
  });

  await test.step('the link shows the same figures to someone who is not signed in', async () => {
    const ownerKpis = await page.locator('app-kpi-cards article .value').allInnerTexts();
    const visitor = await browser.newContext();
    const shared = await visitor.newPage();
    await shared.goto(shareUrl);

    await expect(shared.getByRole('heading', { level: 1 })).toHaveText(viewName);
    await expect(shared.locator('app-kpi-cards article .value')).toHaveText(ownerKpis);
    await expect(shared.locator('a')).toHaveCount(0); // read-only: no links to order rows
    await visitor.close();
  });

  await test.step('revoking the link in Settings ends it', async () => {
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Settings' }).click();
    await page.getByRole('button', { name: `Revoke link to ${viewName}` }).click();
    await expect(page.locator('app-shared-links [role="status"]')).toHaveText(`The link to ${viewName} no longer works.`);

    const visitor = await browser.newContext();
    const shared = await visitor.newPage();
    await shared.goto(shareUrl);
    await expect(shared.getByRole('heading', { level: 1 })).toHaveText('This link no longer works');
    await visitor.close();
  });

  await test.step('reset the demo data so the shared demo is left as it was', async () => {
    await page.getByRole('button', { name: 'Reset demo data' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Reset data' }).click();
    await expect(page.locator('section[aria-labelledby="reset-title"] [role="status"]')).toContainText('order lines loaded', { timeout: 60_000 });
  });

  expect(consoleErrors).toEqual([]);
});
