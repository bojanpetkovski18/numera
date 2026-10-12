import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

/**
 * The main flow from the spec: sign in, import a CSV with good and bad rows, see the dashboard update, drill down to
 * the grid, export. Ends with the demo reset, so the shared demo is left as it was found.
 *
 * The imported rows use a region name unique to this run, so the drill-down and the export can be checked exactly
 * whatever else is in the data.
 */
const SEEDED_ORDERS = 110_579;
const run = Date.now().toString(36).toUpperCase();
const region = `E2E Region ${run}`;
const csv = [
  'order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status',
  `E2E-${run}-1,2026-09-15,C-E2E01,${region},Online Store,HP-SO-ETH,Single Origin,2,18.50,0,completed`,
  `E2E-${run}-2,2026-09-16,C-E2E02,${region},Wholesale,HP-BL-PIN,Blends,10,15.00,0.1,completed`,
  `E2E-${run}-3,2026-09-17,C-E2E03,${region},Online Store,HP-SO-KEN,Single Origin,0,19.50,0,completed`, // quantity 0: rejected
].join('\r\n');

function kpi(page: Page, label: string) {
  return page.locator('app-kpi-cards article').filter({ has: page.getByRole('heading', { name: label, exact: true }) }).locator('.value');
}

async function ordersKpi(page: Page): Promise<number> {
  await expect(kpi(page, 'Orders')).toHaveText(/^\d[\d,]*$/);
  return Number((await kpi(page, 'Orders').innerText()).replace(/,/g, ''));
}

test('sign in, import, dashboard, drill down, export, reset', async ({ page }) => {
  await test.step('sign in with the demo account', async () => {
    await page.goto('/login');
    await page.getByRole('button', { name: 'Fill in demo credentials' }).click();
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();
  });

  const ordersBefore = await ordersKpi(page);

  await test.step('import a CSV with two good rows and one bad row', async () => {
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Import' }).click();
    await page.locator('#file-input').setInputFiles({ name: `e2e-${run}.csv`, mimeType: 'text/csv', buffer: Buffer.from(csv) });

    const result = page.getByRole('region', { name: 'Import result' });
    await expect(result.getByRole('heading', { name: 'Imported with rejected rows' })).toBeVisible();
    await expect(result.locator('.counts')).toContainText(/Rows read\s*3/);
    await expect(result.locator('.counts')).toContainText(/Imported\s*2/);
    await expect(result.locator('.counts')).toContainText(/Rejected\s*1/);
  });

  await test.step('the dashboard counts the two new orders', async () => {
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Dashboard' }).click();
    await expect.poll(() => ordersKpi(page)).toBe(ordersBefore + 2);
  });

  await test.step('drill down from the region breakdown to exactly the imported rows', async () => {
    const byRegion = page.locator('app-breakdown-chart').filter({ has: page.getByRole('heading', { name: 'Revenue by region' }) });
    await byRegion.getByText('Show data table').click();
    await byRegion.getByRole('link', { name: region, exact: true }).click();

    await expect(page).toHaveURL(/\/orders\?/);
    await expect(page.locator('.summary')).toHaveText('1–2 of 2 order lines');
    await expect(page.locator('app-filter-chips')).toContainText(`Region: ${region}`);
    await expect(page.locator('app-filter-chips')).toContainText('Status: Completed');
    const ids = await page.locator('table.orders tbody td:nth-child(2)').allInnerTexts();
    expect(ids.sort()).toEqual([`E2E-${run}-1`, `E2E-${run}-2`]);
  });

  await test.step('export the drilled view as CSV', async () => {
    const download = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Export CSV' }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/^numera-orders_\d{8}-\d{6}Z_.*e2e-region.*_completed\.csv$/);

    const text = readFileSync(await file.path(), 'utf8');
    const lines = text.replace(/^﻿/, '').trimEnd().split('\r\n');
    expect(lines[0]).toBe('order_id,order_date,customer_id,region,channel,product_sku,product_category,quantity,unit_price,discount,status,line_total');
    expect(lines.slice(1).sort()).toEqual([
      `E2E-${run}-1,2026-09-15,C-E2E01,${region},Online Store,HP-SO-ETH,Single Origin,2,18.50,0,completed,37.00`,
      `E2E-${run}-2,2026-09-16,C-E2E02,${region},Wholesale,HP-BL-PIN,Blends,10,15.00,0.1,completed,135.00`,
    ]);
    await expect(page.locator('app-export-button [role="status"]')).toContainText('2 order lines exported');
  });

  await test.step('reset the demo data so the shared demo is left as it was', async () => {
    await page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Settings' }).click();
    await page.getByRole('button', { name: 'Reset demo data' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Reset data' }).click();
    await expect(page.getByRole('status')).toContainText('order lines loaded', { timeout: 60_000 });

    // A fresh load, so the drilled filters from earlier do not carry over. The seed is deterministic, so the reset
    // dashboard shows the seeded order count.
    await page.goto('/dashboard');
    await expect.poll(() => ordersKpi(page)).toBe(SEEDED_ORDERS);
  });
});
