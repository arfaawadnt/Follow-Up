import { defineConfig, devices } from '@playwright/test';
import { resolve } from 'path';

/**
 * E2E config for the Follow-Up SPA served by the API on :5088.
 * The webServer block launches the API (inheriting FOLLOWUP_DB / FOLLOWUP_AUTH_SECRET from the environment)
 * and waits on the health endpoint; reuseExistingServer lets a locally-running instance be reused.
 * Paths are resolved from this file's location so the config stays machine-portable.
 * Run with: FOLLOWUP_DB=... FOLLOWUP_AUTH_SECRET=... npx playwright test
 */
const apiDir = resolve(__dirname, '../src/FollowUp.Api');
const apiDll = resolve(apiDir, 'bin/Debug/net8.0/FollowUp.Api.dll');

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  expect: { timeout: 8_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5088',
    headless: true,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: `dotnet "${apiDll}"`,
    cwd: apiDir,
    url: 'http://localhost:5088/healthz/live',
    reuseExistingServer: true,
    timeout: 120_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Production',
      ASPNETCORE_CONTENTROOT: apiDir,
      ASPNETCORE_URLS: 'http://localhost:5088',
    },
  },
});
