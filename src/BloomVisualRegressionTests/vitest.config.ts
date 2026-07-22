import { defineConfig } from "vitest/config";

// These tests drive a real Bloom instance (selecting books, changing branding/theme,
// screenshotting), so both the per-test work and the beforeAll/afterAll hooks take far
// longer than vitest's defaults (5s test, 10s hook). Raise both generously. Note that
// hookTimeout cannot be set from the CLI in vitest 0.34, so it must live here.
export default defineConfig({
    test: {
        testTimeout: 5_000_000,
        hookTimeout: 600_000,
    },
});
