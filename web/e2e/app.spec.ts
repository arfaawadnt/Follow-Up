import { test, expect, Page } from '@playwright/test';

const ADMIN = process.env.E2E_ADMIN_USER ?? 'admin';
const PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'Seed_Admin_2026!';

async function signIn(page: Page, password = PASSWORD): Promise<void> {
  await page.fill('input[formControlName="username"]', ADMIN);
  await page.fill('input[formControlName="password"]', password);
  await page.click('button[type="submit"]');
}

test.describe('Follow-Up SPA', () => {
  test('unauthenticated visit is redirected to login', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login/);
    await expect(page.locator('button[type="submit"]')).toBeVisible();
  });

  test('rejects bad credentials with an error banner', async ({ page }) => {
    await page.goto('/login');
    await signIn(page, 'definitely-wrong');
    await expect(page.locator('.inline-banner-error')).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });

  test('signs in and lands on the dashboard', async ({ page }) => {
    await page.goto('/');
    await signIn(page);
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });
    await expect(page.getByRole('link', { name: /Laboratories/i })).toBeVisible();
  });

  test('navigates to the laboratories list', async ({ page }) => {
    await page.goto('/');
    await signIn(page);
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });
    await page.getByRole('link', { name: /Laboratories/i }).click();
    await expect(page).toHaveURL(/\/labs/);
    await expect(page.getByRole('heading', { name: /Laboratories/i })).toBeVisible();
  });
});
