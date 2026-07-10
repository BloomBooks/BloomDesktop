import { defineConfig } from "playwright/test";

// Config for the "bloom-exe" CDP suite: Playwright attaches to a real, running Bloom.exe
// (WebView2) over CDP rather than spinning up its own browser/server. These tests import
// from the repo-root `playwright/test`, so — unlike the component-tester's vite-based
// suite — no NODE_PATH/@playwright/test juggling is needed here.
//
// Run it from src/BloomBrowserUI with Bloom already running, e.g.:
//   pnpm exec playwright test --config react_components/component-tester/playwright.bloom-exe.config.ts
export default defineConfig({
    // Discover bloom-exe CDP tests anywhere under BloomBrowserUI (not just
    // react_components) so a feature's e2e can live next to its code. testMatch keeps
    // the set to bloom-exe*.uitest.ts; testIgnore prunes vendored/build trees so the
    // file crawl stays fast.
    testDir: "../..",
    testMatch: "**/bloom-exe*.uitest.ts",
    testIgnore: ["**/node_modules/**", "**/lib/**", "**/output/**"],
    timeout: 30000,
    workers: 1,
    expect: {
        timeout: 5000,
    },
    use: {
        trace: "on-first-retry",
    },
});
