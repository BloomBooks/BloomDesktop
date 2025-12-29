/* eslint-env node */
/* global console, process */
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const { LessWatchManager } = require("./watchLessManager.js");

export { LessWatchManager };

async function runWatcherFromCli() {
    const __filename = fileURLToPath(import.meta.url);
    const __dirname = path.dirname(__filename);
    const browserUIRoot = path.resolve(__dirname, "..");
    const repoRoot = path.resolve(browserUIRoot, "..", "..");
    const contentRoot = path.resolve(browserUIRoot, "..", "content");
    const outputRoot = path.resolve(
        browserUIRoot,
        "..",
        "..",
        "output",
        "browser",
    );

    const scopeArg = process.argv.find((arg) => arg.startsWith("--scope="));
    const scope = scopeArg ? scopeArg.split("=")[1] : "all";
    const once = process.argv.includes("--once");

    const targets = [];
    if (scope === "all" || scope === "browser-ui") {
        targets.push({
            name: "browser-ui",
            root: browserUIRoot,
            outputBase: outputRoot,
        });
    }
    if (scope === "all" || scope === "content") {
        targets.push(
            {
                name: "branding",
                root: path.join(contentRoot, "branding"),
                outputBase: path.join(outputRoot, "branding"),
            },
            {
                name: "templates",
                root: path.join(contentRoot, "templates"),
                outputBase: path.join(outputRoot, "templates"),
            },
            {
                name: "bookLayout",
                root: path.join(contentRoot, "bookLayout"),
                outputBase: path.join(outputRoot, "bookLayout"),
                entries: ["basePage.less", "canvasElement.less"],
            },
        );
    }

    if (targets.length === 0) {
        console.error(`Unknown scope "${scope}" supplied to watchLess`);
        process.exit(1);
    }

    const metadataPath = path.join(outputRoot, ".less-watch-state.json");

    const quietLogger = {
        log: () => {},
        warn: (...args) => console.warn(...args),
        error: (...args) => console.error(...args),
    };

    const manager = new LessWatchManager({
        repoRoot,
        metadataPath,
        targets,
        logger: once ? quietLogger : undefined,
    });

    await manager.initialize();
    if (!once) {
        await manager.startWatching();
    }

    function shutdown() {
        manager
            .dispose()
            .catch((err) => console.error("Failed to stop watchers", err))
            .finally(() => process.exit(0));
    }

    if (!once) {
        process.on("SIGINT", shutdown);
        process.on("SIGTERM", shutdown);
    }
}

const invokedDirectly =
    typeof process !== "undefined" &&
    typeof process.argv?.[1] === "string" &&
    path.basename(process.argv[1]) === "watchLess.mjs";

if (invokedDirectly) {
    runWatcherFromCli().catch((err) => {
        console.error("watchLess failed:", err);
        process.exit(1);
    });
}
