#!/usr/bin/env node

const fs = require("fs").promises;
const path = require("path");

async function postBuild() {
    const outputDir = path.resolve(__dirname, "../../../output/browser");
    const manifestPath = path.join(outputDir, ".vite/manifest.json");

    try {
        // Read the manifest file
        const manifestContent = await fs.readFile(manifestPath, "utf-8");
        const manifest = JSON.parse(manifestContent);

        console.log("Processing manifest for entry points...");

        // Process each entry point
        for (const [entryKey, entryData] of Object.entries(manifest)) {
            // Skip non-entry files (chunks, assets, etc.)
            if (!entryData.isEntry) {
                continue;
            }

            const entryName = path.basename(entryKey, path.extname(entryKey));
            const mainFileName = entryData.file; // This will be X-main.js
            const finalFileName = mainFileName.replace("-main.js", ".js");

            console.log(`Creating ${finalFileName} from ${mainFileName}...`);

            // Collect all dependencies for this entry point
            const dependencies = new Set();

            // Add the main file itself
            dependencies.add("./" + entryData.file);

            // Add CSS files if any
            if (entryData.css && entryData.css.length > 0) {
                entryData.css.forEach((cssFile) => {
                    dependencies.add("./" + cssFile);
                });
            }

            // Add dynamic imports/chunks if any
            if (
                entryData.dynamicImports &&
                entryData.dynamicImports.length > 0
            ) {
                entryData.dynamicImports.forEach((dynamicImport) => {
                    dependencies.add("./" + dynamicImport);
                });
            }

            // Add imports if any
            if (entryData.imports && entryData.imports.length > 0) {
                entryData.imports.forEach((importFile) => {
                    dependencies.add("./" + importFile);
                });
            }

            // Create the final JS file content that imports all dependencies
            let finalContent = "// Auto-generated entry point file\n";
            finalContent +=
                "// This file imports all dependencies for the " +
                entryName +
                " bundle\n\n";

            // Import all dependencies
            const sortedDependencies = Array.from(dependencies).sort();
            sortedDependencies.forEach((dep) => {
                if (dep.endsWith(".css")) {
                    // For CSS files, we'll import them as strings and inject them
                    finalContent += `import '${dep}';\n`;
                } else if (dep.endsWith(".js")) {
                    // For JS files, just import them
                    // Remove underscore prefix from dependency path if present
                    const cleanDep = dep.startsWith("./_")
                        ? "./" + dep.substring(3)
                        : dep;
                    finalContent += `import '${cleanDep}';\n`;
                }
            });

            finalContent += "\n// Entry point loaded successfully\n";
            finalContent += `console.log('${entryName} bundle loaded');\n`;

            // Write the final JS file
            const finalFilePath = path.join(outputDir, finalFileName);
            await fs.writeFile(finalFilePath, finalContent, "utf-8");

            console.log(`✓ Created ${finalFileName}`);
        }

        console.log("Post-build processing completed successfully!");
    } catch (error) {
        console.error("Error during post-build processing:", error);
        process.exit(1);
    }
}

// Run the post-build script
postBuild();
