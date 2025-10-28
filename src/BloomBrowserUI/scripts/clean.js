#!/usr/bin/env node
/**
 * Clean script - deletes the output/browser directory
 * Replaces gulp clean task.
 */

const fs = require("fs");
const path = require("path");

const outputBase = path.resolve(__dirname, "../../../output/browser");

if (fs.existsSync(outputBase)) {
    console.log(`\nCleaning output directory: ${outputBase}`);

    // Delete all files and subdirectories
    const entries = fs.readdirSync(outputBase);
    let deletedCount = 0;

    for (const entry of entries) {
        const fullPath = path.join(outputBase, entry);
        try {
            fs.rmSync(fullPath, { recursive: true, force: true });
            deletedCount++;
        } catch (error) {
            console.error(`Error deleting ${fullPath}:`, error);
            process.exit(1);
        }
    }

    console.log(`✓ Deleted ${deletedCount} items from output directory\n`);
} else {
    console.log(`\nOutput directory does not exist: ${outputBase}`);
    console.log("Nothing to clean.\n");
}
