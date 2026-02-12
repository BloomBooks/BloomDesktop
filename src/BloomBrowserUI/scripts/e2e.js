#!/usr/bin/env node

const { spawnSync } = require("child_process");
const path = require("path");

const suiteName = process.argv[2];
const passthroughArgs = process.argv.slice(3);

const suiteCommands = {
    canvas: [
        "test",
        "--config",
        "./bookEdit/canvas-e2e-tests/playwright.config.ts",
        "--reporter=line",
    ],
};

const printUsage = () => {
    const suites = Object.keys(suiteCommands).join(", ");
    console.error(`Usage: yarn e2e <suite> [playwright args]`);
    console.error(`Available suites: ${suites}`);
};

const assertCurrentPageAvailable = async () => {
    const url = "http://localhost:8089/bloom/CURRENTPAGE";
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 3000);

    try {
        const response = await fetch(url, {
            method: "GET",
            signal: controller.signal,
        });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
    } catch (error) {
        console.error(
            `Cannot reach ${url}. Start Bloom so CURRENTPAGE is available, then rerun \'yarn e2e canvas\'.`,
        );
        console.error(error instanceof Error ? error.message : String(error));
        process.exit(1);
    } finally {
        clearTimeout(timeoutId);
    }
};

const run = async () => {
    if (!suiteName || !suiteCommands[suiteName]) {
        printUsage();
        process.exit(1);
    }

    if (suiteName === "canvas") {
        await assertCurrentPageAvailable();
    }

    const playwrightPackageJsonPath = require.resolve(
        "playwright/package.json",
    );
    const playwrightCliPath = path.join(
        path.dirname(playwrightPackageJsonPath),
        "cli.js",
    );
    const result = spawnSync(
        process.execPath,
        [playwrightCliPath, ...suiteCommands[suiteName], ...passthroughArgs],
        {
            stdio: "inherit",
        },
    );

    if (result.error) {
        console.error(result.error.message);
        process.exit(1);
    }

    if (typeof result.status === "number") {
        process.exit(result.status);
    }

    process.exit(1);
};

void run();
