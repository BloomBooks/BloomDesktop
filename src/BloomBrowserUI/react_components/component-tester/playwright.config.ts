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
    timeout: 5000,
    expect: {
        timeout: 1000,
    },
    use: {
        baseURL: "http://127.0.0.1:5183",
        trace: "on-first-retry",
    },
    // Spin up the Vite dev server so the harness is available during tests.
    webServer: {
        command: "yarn dev",
        cwd: __dirname,
        url: "http://127.0.0.1:5183",
        reuseExistingServer: true,
        stdout: "pipe",
        stderr: "pipe",
        timeout: 120_000,
    },
};

export default config;
