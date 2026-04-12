/// <reference types="node" />
import { defineConfig } from "@playwright/test";
import { readFileSync } from "fs";
import { resolve } from "path";

function loadEnvFile(filePath: string): Record<string, string> {
  const content = readFileSync(filePath, "utf-8");
  const result: Record<string, string> = {};
  for (const line of content.split("\n")) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const idx = trimmed.indexOf("=");
    if (idx === -1) continue;
    result[trimmed.slice(0, idx).trim()] = trimmed.slice(idx + 1).trim();
  }
  return result;
}

const testEnv = loadEnvFile(resolve(process.cwd(), "server/.env.test"));

export default defineConfig({
  testDir: "./e2e",
  outputDir: "./e2e/test-results",
  globalSetup: "./e2e/global-setup.ts",
  globalTeardown: "./e2e/global-teardown.ts",
  reporter: process.env.CI ? "github" : "html",
  use: {
    baseURL: "http://localhost:5175",
    storageState: "e2e/.auth/user.json",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  webServer: [
    {
      command: "bun src/index.ts",
      cwd: "server",
      port: Number(testEnv.PORT),
      env: testEnv,
      reuseExistingServer: !process.env.CI,
    },
    {
      command: "bunx vite --port 5175",
      cwd: "client",
      port: 5175,
      env: { API_URL: testEnv.BETTER_AUTH_URL },
      reuseExistingServer: !process.env.CI,
    },
  ],
});
