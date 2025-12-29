/* eslint-env node */
/* global console, process */
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createRequire } from "node:module";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const require = createRequire(import.meta.url);
const { LessWatchManager } = require("./watchLessManager.js");

function resolvePaths(options = {}) {
    const browserUIRoot =
        options.browserUIRoot ?? path.resolve(__dirname, "..");
    const outputBase =
        options.outputBase ??
        path.resolve(browserUIRoot, "../../output/browser");

    const repoRoot =
        options.repoRoot ?? path.resolve(browserUIRoot, "..", "..");
    const metadataPath =
        options.metadataPath ?? path.join(outputBase, ".less-watch-state.json");

    return { browserUIRoot, outputBase, repoRoot, metadataPath };
}

export async function compileLessFiles(options = {}) {
    const { browserUIRoot, outputBase, repoRoot, metadataPath } =
        resolvePaths(options);

    const contentRoot =
        options.contentRoot ?? path.resolve(browserUIRoot, "..", "content");

    let compiled = 0;
    const logger = {
        ...console,
        log: (...args) => {
            if (typeof args[0] === "string" && args[0].startsWith("[LESS] ✓")) {
                compiled++;
            }
            console.log(...args);
        },
    };

    const targets = options.targets ?? [
        {
            name: "browser-ui",
            root: browserUIRoot,
            outputBase,
        },
        {
            name: "branding",
            root: path.join(contentRoot, "branding"),
            outputBase: path.join(outputBase, "branding"),
        },
        {
            name: "templates",
            root: path.join(contentRoot, "templates"),
            outputBase: path.join(outputBase, "templates"),
        },
        {
            name: "bookLayout",
            root: path.join(contentRoot, "bookLayout"),
            outputBase: path.join(outputBase, "bookLayout"),
            entries: ["basePage.less", "canvasElement.less"],
        },
    ];

    const manager = new LessWatchManager({
        repoRoot,
        metadataPath,
        targets,
        logger,
    });

    await manager.initialize();
    const total = manager.entries?.size ?? 0;
    const skipped = Math.max(0, total - compiled);
    console.log(
        `Less: ${compiled} compiled, ${skipped} up-to-date (${total} total)\n`,
    );
}

const invokedDirectly =
    typeof process !== "undefined" &&
    typeof process.argv?.[1] === "string" &&
    path.basename(process.argv[1]) === "compileLess.mjs";

if (invokedDirectly) {
    compileLessFiles().catch((err) => {
        console.error("Failed to compile LESS files:", err);
        process.exitCode = 1;
    });
}
