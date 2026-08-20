import { test, expect, Page } from '@playwright/test';

const ADMIN = process.env.E2E_ADMIN_USER ?? 'admin';
const PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'Seed_Admin_2026!';

async function signIn(page: Page, password = PASSWORD): Promise<void> {
  await page.fill('input[name="username"]', ADMIN);
  await page.fill('input[name="password"]', password);
  await page.click('.login-btn');
}

test.describe('Follow-Up SPA', () => {
  test('unauthenticated visit is redirected to login', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login/);
    await expect(page.locator('.login-btn')).toBeVisible();
  });

  test('rejects bad credentials with an error banner', async ({ page }) => {
    await page.goto('/login');
    await signIn(page, 'definitely-wrong');
    await expect(page.locator('.err')).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });

  test('signs in and lands on the dashboard', async ({ page }) => {
    await page.goto('/');
    await signIn(page);
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });
    // Sidebar "Core Operations" group is expanded by default → Dashboard item is visible.
    await expect(page.getByRole('link', { name: /Dashboard/i })).toBeVisible();
  });

  test('navigates to the daily board from the sidebar', async ({ page }) => {
    await page.goto('/');
    await signIn(page);
    await expect(page).toHaveURL(/\/dashboard/, { timeout: 15_000 });
    await page.getByRole('link', { name: /Daily/i }).first().click();
    await expect(page).toHaveURL(/\/daily/);
  });
});
