import { defineConfig } from "playwright/test";

const config = defineConfig({
    testDir: "./specs",
    testMatch: "**/*.spec.ts",
    timeout: 30000,
    retries: 1,
    expect: {
        timeout: 5000,
    },
    use: {
        trace: "on-first-retry",
    },
});

export default config;
