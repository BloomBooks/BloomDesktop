import { defineConfig } from "@playwright/test";
import baseConfig from "../playwright.config";

// Scripts that drive a real Bloom through the e2e fixtures, run only when someone asks for them:
//
//   pnpm exec playwright test --config scripts/playwright.config.ts
//
// They live outside tests/, so the nightly suite (which runs ../playwright.config.ts) never
// sees them. They take longer than a test is allowed to, so each sets its own timeout.
export default defineConfig({
    ...baseConfig,
    testDir: ".",
    testMatch: "*.script.ts",
});
