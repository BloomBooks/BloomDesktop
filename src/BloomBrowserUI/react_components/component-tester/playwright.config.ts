import * as path from "path";
import type { PlaywrightTestConfig } from "@playwright/test";

process.env.BLOOM_COMPONENT_TESTER_SUPPRESS_OPEN = "1";
process.env.VITE_OPEN = "false";

// Force Node to resolve Playwright from this package's node_modules so sibling spec files
// don't pull in the repo-level copy and trigger the "Requiring @playwright/test second time" error.

// Make sure our local node_modules is searched before anything inherited from the parent repo.
const nodeModulesPath = path.resolve(__dirname, "node_modules");
const currentNodePath = process.env.NODE_PATH
    ? process.env.NODE_PATH.split(path.delimiter)
    : [];

if (!currentNodePath.includes(nodeModulesPath)) {
    process.env.NODE_PATH = [nodeModulesPath, ...currentNodePath]
        .filter(Boolean)
        .join(path.delimiter);
}

// Clear any cached versions of @playwright/test to ensure we use the local one
// eslint-disable-next-line @typescript-eslint/no-require-imports
Object.keys(require.cache).forEach((key) => {
    if (key.includes("@playwright/test") || key.includes("playwright/lib")) {
        delete require.cache[key];
    }
});

const config: PlaywrightTestConfig = {
    testDir: "..",
    testMatch: "**/*.uitest.*",
    // The bloom-exe*.uitest.ts specs belong to the other config
    // (playwright.bloom-exe.config.ts): they attach over CDP to a running Bloom.exe and
    // import from the repo-level `playwright/test` rather than this package's
    // @playwright/test. Collecting them here loads a second copy of Playwright, which
    // fails hard with "Requiring @playwright/test second time" and aborts the whole run
    // before any component test executes. node_modules is pruned so the crawl stays fast.
    testIgnore: ["**/bloom-exe*.uitest.*", "**/node_modules/**"],
    // Allow extra time for the first Vite build when the harness is cold.
    timeout: 30000,
    expect: {
        // This is the retry budget for web-first assertions, not a fixed wait: a passing
        // assertion still returns as soon as it is true, so raising it costs nothing on
        // green runs. It was 1000ms, which was too tight to wait for a component's first
        // mount while the dev server was still transforming its module graph — roughly one
        // test per full-suite run failed with "element(s) not found", a different one each
        // time, while passing in isolation. 5000ms matches playwright.bloom-exe.config.ts.
        // The overall test timeout above is raised in step so that a genuinely broken
        // assertion still reports as an assertion failure rather than a whole-test timeout.
        timeout: 5000,
    },
    use: {
        baseURL: "http://127.0.0.1:5183",
        trace: "on-first-retry",
    },
    // Spin up the Vite dev server so the harness is available during tests.
    webServer: {
        command: "pnpm dev",
        cwd: __dirname,
        url: "http://127.0.0.1:5183",
        reuseExistingServer: true,
        stdout: "pipe",
        stderr: "pipe",
        timeout: 120_000,
    },
};

export default config;
