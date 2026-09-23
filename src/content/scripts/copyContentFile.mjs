/* eslint-env node */
import path from "node:path";
import * as fs from "node:fs";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const contentRoot = path.resolve(__dirname, "..");
const outputRoot = path.resolve(contentRoot, "../../output/browser");

function needsCopy(sourceFile, destinationFile) {
    if (!fs.existsSync(destinationFile)) {
        return true;
    }

    const sourceStat = fs.statSync(sourceFile);
    const destinationStat = fs.statSync(destinationFile);

    if (sourceStat.size !== destinationStat.size) {
        return true;
    }

    return sourceStat.mtimeMs > destinationStat.mtimeMs;
}

function copyContentFile(filePath, sourceBase, destinationBase, options = {}) {
    const quiet = options.quiet ?? true;
    if (!filePath || !sourceBase) {
        return false;
    }

    const absolutePath = path.resolve(filePath);

    if (!fs.existsSync(absolutePath)) {
        return false;
    }

    const stat = fs.statSync(absolutePath);
    if (stat.isDirectory()) {
        return false;
    }

    const sourceBasePath = path.resolve(contentRoot, sourceBase);
    if (
        !absolutePath.startsWith(sourceBasePath + path.sep) &&
        absolutePath !== sourceBasePath
    ) {
        return false;
    }

    const relativePath = path.relative(sourceBasePath, absolutePath);
    const destinationRoot = path.resolve(outputRoot, destinationBase || ".");
    const destinationFile = path.join(destinationRoot, relativePath);

    if (!needsCopy(absolutePath, destinationFile)) {
        return false;
    }

    const destinationDir = path.dirname(destinationFile);
    if (!fs.existsSync(destinationDir)) {
        fs.mkdirSync(destinationDir, { recursive: true });
    }

    fs.copyFileSync(absolutePath, destinationFile);

    const fromDisplay = path
        .relative(contentRoot, absolutePath)
        .replace(/\\/g, "/");
    const toDisplay = path
        .relative(outputRoot, destinationFile)
        .replace(/\\/g, "/");

    if (!quiet) {
        console.log(`  ✓ Copied ${fromDisplay} -> ${toDisplay}`);
    }
    return true;
}

const invokedDirectly =
    typeof process !== "undefined" &&
    typeof process.argv?.[1] === "string" &&
    path.basename(process.argv[1]) === "copyContentFile.mjs";

if (invokedDirectly) {
    const args = process.argv.slice(2);
    const verbose = args.includes("--verbose");
    const filtered = args.filter((arg) => arg !== "--verbose");
    const [filePath, sourceBase = ".", destinationBase = "."] = filtered;
    copyContentFile(filePath, sourceBase, destinationBase, {
        quiet: !verbose,
    });
}

export { copyContentFile };
