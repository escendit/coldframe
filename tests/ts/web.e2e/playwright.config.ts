import { defineConfig, devices } from '@playwright/test';
import { appUrl, ports, unreachableUrl, untrustedUrl } from './fixtures/ports.ts';

export default defineConfig({
  testDir: './specs',
  // One fake IdP with switchable modes is shared by every test, so tests run one at a time.
  workers: 1,
  fullyParallel: false,
  forbidOnly: process.env.CI !== undefined,
  retries: 0,
  reporter: process.env.CI !== undefined ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: appUrl,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'node fixtures/stack.ts',
    // A static file: every page answers a first visit with a 303 that sets the session cookie.
    url: `${appUrl}/_app/version.json`,
    reuseExistingServer: false,
    timeout: 60_000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
  metadata: { ports, appUrl, unreachableUrl, untrustedUrl },
});
