import { defineConfig } from "playwright/test";

// Unlike the canvas suite next door, nothing here talks to a running Bloom: each test
// builds its own page out of the real compiled inlineImages.less, so `pnpm e2e
// inline-images` works from a cold checkout.
const config = defineConfig({
    testDir: "./specs",
    testMatch: "**/*.spec.ts",
    timeout: 30000,
    expect: {
        timeout: 5000,
    },
});

export default config;
