#!/usr/bin/env node

const { spawnSync } = require("child_process");
const path = require("path");

const suiteName = process.argv[2];
const rawPassthroughArgs = process.argv.slice(3);

const parseCanvasModeArgs = (args) => {
    let canvasMode;
    const filteredArgs = [];

    for (const arg of args) {
        if (arg === "--isolated") {
            canvasMode = "isolated";
            continue;
        }

        if (arg === "--shared") {
            canvasMode = "shared";
            continue;
        }

        filteredArgs.push(arg);
    }

    return {
        canvasMode,
        filteredArgs,
    };
};

const { canvasMode, filteredArgs: passthroughArgs } =
    parseCanvasModeArgs(rawPassthroughArgs);

const hasWorkersArg = (args) => {
    return args.some(
        (arg) => arg === "--workers" || arg.startsWith("--workers="),
    );
};

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
    console.error(
        `Usage: yarn e2e <suite> [--isolated|--shared] [playwright args]`,
    );
    console.error(`Available suites: ${suites}`);
    console.error(
        `Canvas mode defaults to shared. Use --isolated for per-test clean-slate page loads.`,
    );
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

    const playwrightArgs = [...suiteCommands[suiteName], ...passthroughArgs];
    if (
        suiteName === "canvas" &&
        (canvasMode ?? "shared") === "shared" &&
        !hasWorkersArg(passthroughArgs)
    ) {
        playwrightArgs.push("--workers=1");
    }

    const result = spawnSync(
        process.execPath,
        [playwrightCliPath, ...playwrightArgs],
        {
            stdio: "inherit",
            env: {
                ...process.env,
                BLOOM_CANVAS_E2E_MODE:
                    suiteName === "canvas"
                        ? (canvasMode ?? "shared")
                        : process.env.BLOOM_CANVAS_E2E_MODE,
            },
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
