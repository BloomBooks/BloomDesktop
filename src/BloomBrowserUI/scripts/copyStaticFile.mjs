/* eslint-env node */
/* global console, process */
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as fs from "node:fs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const excludedExtensions = new Set([
    ".ts",
    ".tsx",
    ".less",
    ".pug",
    ".md",
    ".bat",
]);

function resolvePaths(options = {}) {
    const browserUIRoot =
        options.browserUIRoot ?? path.resolve(__dirname, "..");
    const outputBase =
        options.outputBase ??
        path.resolve(browserUIRoot, "../../output/browser");

    return { browserUIRoot, outputBase };
}

function needsCopy(sourceFile, outputFile) {
    if (!fs.existsSync(outputFile)) {
        return true;
    }
    const sourceStat = fs.statSync(sourceFile);
    const outputStat = fs.statSync(outputFile);
    return sourceStat.mtimeMs > outputStat.mtimeMs;
}

function copyStaticFile(filePath, options = {}) {
    const quiet = options.quiet ?? true;
    if (!filePath) {
        return false;
    }

    const { browserUIRoot, outputBase } = resolvePaths(options);
    const absolutePath = path.resolve(filePath);

    if (!fs.existsSync(absolutePath)) {
        return false;
    }

    const stat = fs.statSync(absolutePath);
    if (stat.isDirectory()) {
        return false;
    }

    if (absolutePath.includes(`${path.sep}node_modules${path.sep}`)) {
        return false;
    }

    const relativePath = path
        .relative(browserUIRoot, absolutePath)
        .replace(/\\/g, "/");
    const fileName = path.basename(relativePath).toLowerCase();

    if (
        fileName === "tsconfig.json" ||
        relativePath.startsWith(".") ||
        excludedExtensions.has(path.extname(relativePath))
    ) {
        return false;
    }

    const outputFile = path.join(outputBase, relativePath);

    if (!needsCopy(absolutePath, outputFile)) {
        return false;
    }

    const outputDir = path.dirname(outputFile);
    if (!fs.existsSync(outputDir)) {
        fs.mkdirSync(outputDir, { recursive: true });
    }

    fs.copyFileSync(absolutePath, outputFile);
    if (!quiet) {
        console.log(`  ✓ Copied ${relativePath}`);
    }
    return true;
}

const invokedDirectly =
    typeof process !== "undefined" &&
    typeof process.argv?.[1] === "string" &&
    path.basename(process.argv[1]) === "copyStaticFile.mjs";

if (invokedDirectly) {
    const args = process.argv.slice(2);
    const verbose = args.includes("--verbose");
    const filtered = args.filter((arg) => arg !== "--verbose");
    copyStaticFile(filtered[0], { quiet: !verbose });
}

export { copyStaticFile };
