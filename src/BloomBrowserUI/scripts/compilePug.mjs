/* eslint-env node */
/* global console, process */
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as fs from "node:fs";
import { glob } from "glob";
import pug from "pug";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function resolvePaths(options = {}) {
    const browserUIRoot =
        options.browserUIRoot ?? path.resolve(__dirname, "..");
    const contentRoot =
        options.contentRoot ?? path.resolve(browserUIRoot, "../content");
    const outputBase =
        options.outputBase ??
        path.resolve(browserUIRoot, "../../output/browser");

    return { browserUIRoot, contentRoot, outputBase };
}

function getMTime(filePath) {
    try {
        return fs.statSync(filePath).mtimeMs;
    } catch {
        return 0;
    }
}

function needsRebuild(sourceFile, dependencyFiles, outputFile) {
    if (!fs.existsSync(outputFile)) {
        return true;
    }

    const outputTime = getMTime(outputFile);
    const timesToCheck = [sourceFile, ...(dependencyFiles ?? [])];
    for (const dep of timesToCheck) {
        const depTime = getMTime(dep);
        if (!depTime || depTime > outputTime) {
            return true;
        }
    }

    return false;
}

export async function compilePugFiles(options = {}) {
    const { browserUIRoot, contentRoot, outputBase } = resolvePaths(options);
    const {
        logSummary = false,
        logWhenNoChanges = false,
        logFiles = false,
    } = options;

    const browserUIPugFiles = glob.sync("**/*.pug", {
        cwd: browserUIRoot,
        ignore: ["**/node_modules/**", "**/*mixins.pug"],
        nodir: true,
        absolute: true,
    });

    const contentPugFiles = glob.sync("**/*.pug", {
        cwd: contentRoot,
        ignore: ["**/node_modules/**", "**/*mixins.pug"],
        nodir: true,
        absolute: true,
    });

    const allPugFiles = [...browserUIPugFiles, ...contentPugFiles];

    let compiled = 0;
    let skipped = 0;

    for (const file of allPugFiles) {
        const isContentFile = file.startsWith(contentRoot + path.sep);
        const baseRoot = isContentFile ? contentRoot : browserUIRoot;
        const relativePath = path
            .relative(baseRoot, file)
            .replace(/\\/g, "/")
            .replace(/\.pug$/i, ".html");

        const outputFile = path.join(outputBase, relativePath);

        const compiledTemplate = pug.compileFile(file, {
            basedir: baseRoot,
            pretty: true,
        });

        const dependencies = Array.from(
            new Set(
                (compiledTemplate.dependencies ?? []).map((dep) =>
                    path.resolve(dep),
                ),
            ),
        );

        if (!needsRebuild(file, dependencies, outputFile)) {
            skipped++;
            continue;
        }

        const outputDir = path.dirname(outputFile);
        if (!fs.existsSync(outputDir)) {
            fs.mkdirSync(outputDir, { recursive: true });
        }

        const html = compiledTemplate({});

        fs.writeFileSync(outputFile, html);
        if (logFiles) {
            const displayPath = path.relative(browserUIRoot, file);
            console.log(`  ✓ ${displayPath} → ${relativePath}`);
        }
        compiled++;
    }

    const total = allPugFiles.length;
    if (logSummary && (logWhenNoChanges || compiled > 0)) {
        console.log(
            `Pug: ${compiled} compiled, ${skipped} up-to-date (${total} total)\n`,
        );
    }

    return { compiled, skipped, total };
}

const invokedDirectly =
    typeof process !== "undefined" &&
    typeof process.argv?.[1] === "string" &&
    path.basename(process.argv[1]) === "compilePug.mjs";

if (invokedDirectly) {
    const args = process.argv.slice(2);
    const verbose = args.includes("--verbose");
    compilePugFiles({
        logSummary: verbose,
        logWhenNoChanges: verbose,
        logFiles: verbose,
    }).catch((err) => {
        console.error("Failed to compile Pug files:", err);
        process.exitCode = 1;
    });
}
