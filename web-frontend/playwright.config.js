import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./tests",
  outputDir: "../temp/browser-results",
  timeout: 60000,
  workers: 1,
  use: {
    baseURL: process.env.BASE_URL || "http://localhost:19440",
    channel: "msedge",
    viewport: { width: 1440, height: 1000 },
    screenshot: "only-on-failure",
    headless: true,
  },
  reporter: "list",
});
