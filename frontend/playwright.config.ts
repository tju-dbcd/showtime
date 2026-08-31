import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  // 首个用例需等待 vite 冷启动编译全量模块，测试超时放宽到 120s
  timeout: 120_000,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: process.env.CI ? [
    ['html', { outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'playwright-report/results.json' }],
    ['github']
  ] : [['html', { outputFolder: 'playwright-report' }]],
  use: {
    baseURL: 'http://127.0.0.1:5173',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  // 自动拉起 vite dev server（E2E 全量 mock /api，无需真实后端）
  webServer: {
    command: 'npm run dev -- --port 5173 --host 127.0.0.1',
    url: 'http://127.0.0.1:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
