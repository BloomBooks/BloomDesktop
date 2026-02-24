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
    const repoRoot =
        options.repoRoot ?? path.resolve(browserUIRoot, "..", "..");
    const metadataPath =
        options.metadataPath ?? path.join(outputBase, ".pug-watch-state.json");

    return { browserUIRoot, contentRoot, outputBase, repoRoot, metadataPath };
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

function readJson(filePath) {
    try {
        return JSON.parse(fs.readFileSync(filePath, "utf8"));
    } catch {
        return null;
    }
}

function writeJsonAtomic(filePath, data) {
    const dirPath = path.dirname(filePath);
    if (!fs.existsSync(dirPath)) {
        fs.mkdirSync(dirPath, { recursive: true });
    }
    const tmpPath = `${filePath}.tmp`;
    fs.writeFileSync(tmpPath, JSON.stringify(data, null, 2));
    fs.renameSync(tmpPath, filePath);
}

export async function compilePugFiles(options = {}) {
    const { browserUIRoot, contentRoot, outputBase, repoRoot, metadataPath } =
        resolvePaths(options);
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

    const allPugFiles = [
        ...browserUIPugFiles.map((file) => ({ file, baseRoot: browserUIRoot })),
        ...contentPugFiles.map((file) => ({ file, baseRoot: contentRoot })),
    ];

    const metadataVersion = 1;
    const existingMetadata = readJson(metadataPath);
    const cachedEntries =
        existingMetadata?.version === metadataVersion
            ? (existingMetadata.entries ?? {})
            : {};
    const nextEntries = {};

    let compiled = 0;
    let skipped = 0;

    for (const { file, baseRoot } of allPugFiles) {
        const relativePath = path
            .relative(baseRoot, file)
            .replace(/\\/g, "/")
            .replace(/\.pug$/i, ".html");

        const outputFile = path.join(outputBase, relativePath);
        const entryId = path.relative(repoRoot, file).replace(/\\/g, "/");

        const cachedDependencies = (cachedEntries[entryId] ?? []).map((dep) =>
            path.resolve(repoRoot, dep),
        );

        if (!needsRebuild(file, cachedDependencies, outputFile)) {
            nextEntries[entryId] = cachedEntries[entryId] ?? [];
            skipped++;
            continue;
        }

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

        nextEntries[entryId] = dependencies.map((dep) =>
            path.relative(repoRoot, dep).replace(/\\/g, "/"),
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

    writeJsonAtomic(metadataPath, {
        version: metadataVersion,
        entries: nextEntries,
    });

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
