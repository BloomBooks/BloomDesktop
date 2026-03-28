import * as path from "path";
import type { PlaywrightTestConfig } from "@playwright/test";

const nodeModulesPath = path.resolve(__dirname, "node_modules");
const currentNodePath = process.env.NODE_PATH
    ? process.env.NODE_PATH.split(path.delimiter)
    : [];

if (!currentNodePath.includes(nodeModulesPath)) {
    process.env.NODE_PATH = [nodeModulesPath, ...currentNodePath]
        .filter(Boolean)
        .join(path.delimiter);
}

// eslint-disable-next-line @typescript-eslint/no-require-imports
Object.keys(require.cache).forEach((key) => {
    if (key.includes("@playwright/test") || key.includes("playwright/lib")) {
        delete require.cache[key];
    }
});

const config: PlaywrightTestConfig = {
    testDir: "..",
    testMatch: "**/bloom-exe*.uitest.ts",
    timeout: 30000,
    workers: 1,
    expect: {
        timeout: 5000,
    },
    use: {
        trace: "on-first-retry",
    },
};

export default config;
