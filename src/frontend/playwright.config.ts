import { randomBytes } from 'node:crypto';
import { rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig, devices } from '@playwright/test';

const frontendRoot = dirname(fileURLToPath(import.meta.url));
const runNamespace = process.env.TRANSCRIPT_LAB_E2E_RUN_NAMESPACE ?? randomBytes(12).toString('hex');
const runOutputDir = process.env.TRANSCRIPT_LAB_E2E_OUTPUT_DIR
  ?? join(tmpdir(), `transcript-lab-playwright-${runNamespace}`);
const buildOutputDir = process.env.TRANSCRIPT_LAB_E2E_BUILD_DIR ?? join(frontendRoot, 'dist');
const directOutputIsOwned = dirname(runOutputDir) === tmpdir()
  && basename(runOutputDir) === `transcript-lab-playwright-${runNamespace}`;
const harnessRoot = dirname(runOutputDir);
const harnessOutputIsOwned = dirname(harnessRoot) === tmpdir()
  && basename(harnessRoot) === `transcript-lab-e2e-${runNamespace}`
  && basename(runOutputDir) === 'test-results';
if (!directOutputIsOwned && !harnessOutputIsOwned) {
  throw new Error('Invalid Playwright output namespace.');
}

process.once('exit', () => {
  rmSync(runOutputDir, { force: true, recursive: true });
});

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  outputDir: runOutputDir,
  reporter: 'list',
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  use: {
    headless: true,
  },
  webServer: {
    command: `npm run preview -- --outDir ${JSON.stringify(buildOutputDir)} --host 127.0.0.1 --port 0 --strictPort`,
    wait: {
      stdout: /http:\/\/127\.0\.0\.1:(?<frontend_port>\d+)\//,
    },
    reuseExistingServer: false,
  },
});
