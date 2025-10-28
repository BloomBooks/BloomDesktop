#!/usr/bin/env node
/**
 * Localization build script
 * Handles translation of HTML files and creation of XLIFF files for localization.
 * Replaces gulp build-l10n task.
 */

const { glob } = require("glob");
const path = require("path");
const fs = require("fs");
const { execSync } = require("child_process");

// Detect if we're on Linux
const isLinux = fs.existsSync("/opt/mono5-sil");

// Get all HTML files created by markdown or pug
const htmlFiles = glob.sync("../../output/browser/**/*-en.htm*");

// Get all translated Xliff files (excluding English)
const xliffFiles = glob.sync([
    "../../DistFiles/localization/**/*.xlf",
    "!../../DistFiles/localization/en/*.xlf",
    "!../../DistFiles/localization/**/*-en.xlf",
]);

console.log(`Found ${htmlFiles.length} HTML files to process`);
console.log(`Found ${xliffFiles.length} translation files`);

/**
 * Find which translated xliff files match up with the given html file.
 */
function getXliffFiles(htmFile) {
    const pathPieces = htmFile.split(/[/\\]/);
    let basename = pathPieces[pathPieces.length - 1];

    if (basename === "ReadMe-en.htm") {
        basename = "/" + pathPieces[pathPieces.length - 2] + "/ReadMe-";
    } else {
        const pos = basename.search("-en.htm");
        if (pos > 0) basename = "/" + basename.substring(0, pos);
    }

    const retval = [];
    for (let i = 0; i < xliffFiles.length; i++) {
        const pos = xliffFiles[i].search(basename);
        if (pos > 0) retval.push(xliffFiles[i]);
    }
    return retval;
}

/**
 * Get the language code from the xlfFile path and put it into the htmFile path
 * (replacing the English language code) for an output file pathname.
 */
function getOutputFilename(htmFile, xlfFile) {
    let langCode = "";
    if (xlfFile.search("/ReadMe-") > 0) {
        // as in "blah/foo/ReadMe-en.htm"
        langCode = xlfFile.replace(/.*\/ReadMe-/, "").replace(".xlf", "");
    } else {
        // as in "blah/foo/fr/something.htm"
        langCode = xlfFile.split("/").slice(-2, -1)[0]; // penultimate item has the language code
    }
    return htmFile.replace("-en.htm", "-" + langCode + ".htm");
}

/**
 * Get the name of the English Xliff file corresponding to the English HTML file.
 */
function getXliffFilename(htmFile) {
    const htmPieces = htmFile.split(/[/\\]/);
    const basename = htmPieces[htmPieces.length - 1];

    if (basename === "ReadMe-en.htm") {
        return (
            "../../DistFiles/localization/" +
            htmPieces[htmPieces.length - 2] +
            "/" +
            basename.replace(".htm", ".xlf")
        );
    } else {
        return (
            "../../DistFiles/localization/en/" +
            basename.replace(/-en\.html?$/, ".xlf")
        );
    }
}

/**
 * Translate HTML files using XLIFF translation files
 */
function translateHtmlFiles() {
    console.log("\n=== Translating HTML files ===");
    let translationCount = 0;

    for (const htmlFile of htmlFiles) {
        const xliffs = getXliffFiles(htmlFile);

        for (const xliffFile of xliffs) {
            const outfile = getOutputFilename(htmlFile, xliffFile);

            let cmd = "";
            if (isLinux) {
                cmd =
                    "/opt/mono5-sil/bin/mono --debug ../../lib/dotnet/HtmlXliff.exe --inject";
            } else {
                cmd = "..\\..\\lib\\dotnet\\HtmlXliff.exe --inject";
            }
            cmd += ` -x "${xliffFile}"`;
            cmd += ` -o "${outfile}"`;
            cmd += ` "${htmlFile}"`;

            try {
                execSync(cmd, { encoding: "utf8", stdio: "pipe" });
                console.log(
                    `  ✓ Translated ${path.basename(htmlFile)} → ${path.basename(outfile)}`,
                );
                translationCount++;
            } catch (err) {
                console.error(
                    `  ✗ Error translating ${htmlFile} with ${xliffFile}:`,
                );
                if (err.stdout) console.error(err.stdout);
                if (err.stderr) console.error(err.stderr);
            }
        }
    }

    console.log(`\nTranslated ${translationCount} files`);
}

/**
 * Create XLIFF files from English HTML files for translation
 */
function createXliffFiles() {
    console.log("\n=== Creating XLIFF files ===");
    let xliffCount = 0;

    for (const htmlFile of htmlFiles) {
        const xliffFile = getXliffFilename(htmlFile);
        const xliffDir = path.dirname(xliffFile);

        // Ensure output directory exists
        if (!fs.existsSync(xliffDir)) {
            fs.mkdirSync(xliffDir, { recursive: true });
        }

        let cmd = "";
        if (isLinux) {
            cmd =
                "/opt/mono5-sil/bin/mono --debug ../../lib/dotnet/HtmlXliff.exe --extract --preserve";
        } else {
            cmd = "..\\..\\lib\\dotnet\\HtmlXliff.exe --extract --preserve";
        }
        cmd += ` -o "${xliffFile}"`;
        cmd += ` "${htmlFile}"`;

        try {
            execSync(cmd, { encoding: "utf8", stdio: "pipe" });
            console.log(
                `  ✓ Created ${path.basename(xliffFile)} from ${path.basename(htmlFile)}`,
            );
            xliffCount++;
        } catch (err) {
            console.error(`  ✗ Error creating ${xliffFile} from ${htmlFile}:`);
            if (err.stdout) console.error(err.stdout);
            if (err.stderr) console.error(err.stderr);
        }
    }

    console.log(`\nCreated ${xliffCount} XLIFF files`);
}

// Main execution
function main() {
    const args = process.argv.slice(2);

    if (args.includes("--help") || args.includes("-h")) {
        console.log(`
Localization Build Script
=========================

Usage: node scripts/l10n-build.js [command]

Commands:
  (no args)     Run both translate and create tasks
  all           Run both translate and create tasks
  translate     Only translate HTML files using existing XLIFF files
  create        Only create/update XLIFF files from English HTML files

Examples:
  yarn build:l10n              # Run both tasks
  yarn build:l10n:translate    # Only translate
  yarn build:l10n:create       # Only create XLIFF files
        `);
        process.exit(0);
    }

    if (args.length === 0 || args.includes("all")) {
        // Run both tasks
        translateHtmlFiles();
        createXliffFiles();
    } else {
        if (args.includes("translate")) {
            translateHtmlFiles();
        }
        if (args.includes("create")) {
            createXliffFiles();
        }
    }
}

main();
