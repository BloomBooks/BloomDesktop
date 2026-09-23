#!/usr/bin/env node
/**
 * Clean script - deletes the output/browser directory
 * Replaces gulp clean task.
 */

const fs = require("fs");
const path = require("path");

const outputDirs = [path.resolve(__dirname, "../../../output/browser")];
const quiet = process.argv.includes("--quiet");

for (const outputDir of outputDirs) {
    if (!fs.existsSync(outputDir)) {
        if (!quiet) {
            console.log(`\nOutput directory does not exist: ${outputDir}`);
            console.log("Nothing to clean.\n");
        }
        continue;
    }

    if (!quiet) {
        console.log(`\nCleaning output directory: ${outputDir}`);
    }

    const entries = fs.readdirSync(outputDir);
    let deletedCount = 0;

    for (const entry of entries) {
        const fullPath = path.join(outputDir, entry);
        try {
            fs.rmSync(fullPath, { recursive: true, force: true });
            deletedCount++;
        } catch (error) {
            console.error(`Error deleting ${fullPath}:`, error);
            process.exit(1);
        }
    }

    if (!quiet) {
        console.log(`✓ Deleted ${deletedCount} items from output directory\n`);
    }
}
