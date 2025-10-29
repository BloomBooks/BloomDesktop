import { defineConfig } from "vite";
import type { Plugin } from "vite";
import * as path from "path";
import { glob } from "glob";
import react from "@vitejs/plugin-react";
import { viteStaticCopy } from "vite-plugin-static-copy";
import * as fs from "fs";
import pug from "pug";
import less from "less";
import MarkdownIt from "markdown-it";
import markdownItContainer from "markdown-it-container";
import markdownItAttrs from "markdown-it-attrs";
import { playwright } from "@vitest/browser-playwright";

// Custom plugin to compile Pug files to HTML
// There are a couple of npm packages for pug, but as of October2025, they are experimental
// and/or aimed at dynamically serving pug, like vite dev does with js. For now, we're good
// with a simple static compilation during build. Claude sonnet 4.5 came up with this.
// Note that it also builds pug files from ../content. This is because we haven't yet
// changed content to use vite.
function compilePugPlugin(): Plugin {
    return {
        name: "compile-pug",
        apply: "build",
        async closeBundle() {
            // Find pug files in BloomBrowserUI
            const browserUIPugFiles = glob.sync("./**/*.pug", {
                ignore: ["**/node_modules/**", "**/*mixins.pug"],
            });

            // Find pug files in content directory
            const contentPugFiles = glob.sync("../content/**/*.pug", {
                ignore: ["**/node_modules/**", "**/*mixins.pug"],
            });

            const allPugFiles = [...browserUIPugFiles, ...contentPugFiles];

            console.log(
                `\nCompiling ${allPugFiles.length} Pug files (${browserUIPugFiles.length} from BloomBrowserUI, ${contentPugFiles.length} from content)...`,
            );

            const outputBase = path.resolve(__dirname, "../../output/browser");

            for (const file of allPugFiles) {
                // Convert relative path to output path
                // For BloomBrowserUI: "./bookEdit/toolbox/toolbox.pug" -> "../../output/browser/bookEdit/toolbox/toolbox.html"
                // For content: "../content/templates/foo.pug" -> "../../output/browser/templates/foo.html"

                // Normalize path separators for comparison
                const normalizedFile = file.replace(/\\/g, "/");

                let relativePath;
                if (normalizedFile.startsWith("../content/")) {
                    // Strip "../content/" prefix for content files
                    relativePath = normalizedFile
                        .replace("../content/", "")
                        .replace(".pug", ".html");
                } else {
                    // Strip "./" prefix for BloomBrowserUI files
                    relativePath = normalizedFile
                        .replace("./", "")
                        .replace(".pug", ".html");
                }

                const outputFile = path.join(outputBase, relativePath);
                const outputDir = path.dirname(outputFile);

                // Ensure output directory exists
                if (!fs.existsSync(outputDir)) {
                    fs.mkdirSync(outputDir, { recursive: true });
                }

                // Compile pug to HTML
                // Use the appropriate basedir based on the file location
                const basedir = normalizedFile.startsWith("../content/")
                    ? "../content"
                    : ".";
                const html = pug.renderFile(file, {
                    basedir: basedir,
                    pretty: true,
                });

                fs.writeFileSync(outputFile, html);
                console.log(`  ✓ ${file} → ${relativePath}`);
            }

            console.log(`Pug compilation complete!\n`);
        },
    };
}

// Custom plugin to compile LESS files to CSS
// Similar to pug plugin - compiles standalone LESS files to CSS with sourcemaps
// Claude sonnet 4.5 came up with this.
function compileLessPlugin(): Plugin {
    return {
        name: "compile-less",
        apply: "build",
        async closeBundle() {
            // Find LESS files in BloomBrowserUI
            const lessFiles = glob.sync("./**/*.less", {
                ignore: ["**/node_modules/**"],
            });

            console.log(`\nCompiling ${lessFiles.length} LESS files...`);

            const outputBase = path.resolve(__dirname, "../../output/browser");

            for (const file of lessFiles) {
                // Normalize path separators
                const normalizedFile = file.replace(/\\/g, "/");

                // Convert to output path: "./bookEdit/css/editMode.less" -> "bookEdit/css/editMode.css"
                const relativePath = normalizedFile
                    .replace("./", "")
                    .replace(".less", ".css");

                const outputFile = path.join(outputBase, relativePath);
                const outputDir = path.dirname(outputFile);

                // Ensure output directory exists
                if (!fs.existsSync(outputDir)) {
                    fs.mkdirSync(outputDir, { recursive: true });
                }

                try {
                    // Read LESS file
                    const lessContent = fs.readFileSync(file, "utf8");

                    // Compile LESS to CSS with sourcemap
                    const result = await less.render(lessContent, {
                        filename: file,
                        sourceMap: {
                            sourceMapFileInline: false,
                            outputSourceFiles: true,
                            sourceMapURL: path.basename(outputFile) + ".map",
                        },
                    });

                    // Write CSS file with sourcemap reference
                    let cssOutput = result.css;
                    if (result.map) {
                        cssOutput += `\n/*# sourceMappingURL=${path.basename(outputFile)}.map */`;
                    }
                    fs.writeFileSync(outputFile, cssOutput);

                    // Write sourcemap if generated
                    if (result.map) {
                        const mapFile = outputFile + ".map";
                        fs.writeFileSync(mapFile, result.map);
                    }

                    console.log(`  ✓ ${file} → ${relativePath}`);
                } catch (error) {
                    console.error(`  ✗ Error compiling ${file}:`, error);
                    throw error; // Exit build on LESS compilation error
                }
            }

            console.log(`LESS compilation complete!\n`);
        },
    };
}

// Helper function to wrap markdown HTML with optional stylesheet
function wrapMarkdownHtml(html: string, stylesheetHref?: string): string {
    const stylesheetLink = stylesheetHref
        ? `<link rel='stylesheet' href='${stylesheetHref}' type='text/css'/>`
        : "";
    return `<html><head><meta charset='utf-8'>${stylesheetLink}</head><body>\n${html}\n</body></html>`;
}

// Helper function to compile and write a markdown file
function compileMarkdownFile(
    file: string,
    outputFile: string,
    md: MarkdownIt,
    stylesheetHref?: string,
    contentTransform?: (html: string) => string,
): void {
    const content = fs.readFileSync(file, "utf8");
    let html = md.render(content);

    // Apply content transformation if provided (e.g., strip "removethis")
    if (contentTransform) {
        html = contentTransform(html);
    }

    const fullHtml = wrapMarkdownHtml(html, stylesheetHref);

    const outputDir = path.dirname(outputFile);
    if (!fs.existsSync(outputDir)) {
        fs.mkdirSync(outputDir, { recursive: true });
    }

    fs.writeFileSync(outputFile, fullHtml);
}

// Custom plugin to compile Markdown files to HTML
// Handles 4 different types of markdown files with different styling/output
// Claude sonnet 4.5 came up with this, based on our previous gulpfile handling of markdown.
function compileMarkdownPlugin(): Plugin {
    return {
        name: "compile-markdown",
        apply: "build",
        async closeBundle() {
            // Set up markdown-it with extensions matching gulpfile.js
            const md = new MarkdownIt({
                html: true, // enable HTML tags in source
                linkify: true, // autoconvert URL-like text to links
            });
            md.use(markdownItContainer, "warning");
            md.use(markdownItContainer, "info");
            md.use(markdownItContainer, "note");
            md.use(markdownItAttrs);

            const outputBase = path.resolve(__dirname, "../../output/browser");

            // 1. Help files: ./help/**/*.md -> output/browser/help/*.htm (flattened)
            const helpFiles = glob.sync("./help/**/*.md");

            // 2. Template README files: ../content/templates/**/ReadMe*.md -> output/browser/templates/**/ReadMe*.htm
            const templateReadmeFiles = glob.sync(
                "../content/templates/**/ReadMe*.md",
            );

            // 3. Info pages: ./infoPages/*.md -> output/browser/infoPages/*.htm (flattened)
            const infoPageFiles = glob.sync("./infoPages/*.md");

            // 4. Dist info: ../../DistFiles/*.md -> output/browser/*.htm (flattened)
            const distInfoFiles = glob.sync("../../DistFiles/*.md");

            const totalFiles =
                helpFiles.length +
                templateReadmeFiles.length +
                infoPageFiles.length +
                distInfoFiles.length;

            console.log(`\nCompiling ${totalFiles} Markdown files...`);

            // Process help files
            for (const file of helpFiles) {
                const filename = path.basename(file).replace(".md", ".htm");
                const outputFile = path.join(outputBase, "help", filename);
                compileMarkdownFile(file, outputFile, md, "help.css");
                console.log(`  ✓ ${file} → help/${filename}`);
            }

            // Process template README files
            for (const file of templateReadmeFiles) {
                const normalizedFile = file.replace(/\\/g, "/");
                const relativePath = normalizedFile
                    .replace("../content/", "")
                    .replace(".md", ".htm");
                const outputFile = path.join(outputBase, relativePath);
                compileMarkdownFile(
                    file,
                    outputFile,
                    md,
                    "../../../bookPreview/BookReadme.css",
                    (html) => html.replace(/removethis/g, ""), // Strip email obfuscation
                );
                console.log(`  ✓ ${file} → ${relativePath}`);
            }

            // Process info pages
            for (const file of infoPageFiles) {
                const filename = path.basename(file).replace(".md", ".htm");
                const outputFile = path.join(outputBase, "infoPages", filename);
                compileMarkdownFile(file, outputFile, md);
                console.log(`  ✓ ${file} → infoPages/${filename}`);
            }

            // Process dist info files
            for (const file of distInfoFiles) {
                const filename = path.basename(file).replace(".md", ".htm");
                const outputFile = path.join(outputBase, filename);
                compileMarkdownFile(file, outputFile, md);
                console.log(`  ✓ ${file} → ${filename}`);
            }

            console.log(`Markdown compilation complete!\n`);
        },
    };
}

// Plugin to process the Vite manifest and create final bundle files
// The problem this solve is that Vite expects us to have a root HTML file for each entry point.
// Then, as it generates a bunch of optimized chunks of code and CSS to share between entry points,
// it modifies the HTML file for each entry point accordingly.
// But we are not giving vite that sort of control over the HTML files. In many cases, like the
// generated HTML page files that load the editablePageBundle, we can't do so.
// So instead, we tweak the ouput configuration to generate, for each bundle X, xBundle-main.js
// instead of the usual X-bundle.js. Then, in this post-build step, we create a new file X-bundle.js
// which imports all dependencies for XBundle, including the -main.js file. That's then what our
// HTML files load.
// Note: it seems like it should be possible to merge the manifest information into xBundle-main.js
// and make an xBundle.js that would REPLACE xBundle-main.js, reducing the number of files and
// the indirection,but somehow it works out that some of the bundles actually load the root
// -main.js files of OTHER bundles, so modifying or deleting them is not a good option.
function postBuildPlugin(): Plugin {
    interface ManifestEntry {
        file: string;
        isEntry?: boolean;
        css?: string[];
        dynamicImports?: string[];
        imports?: string[];
    }

    return {
        name: "post-build",
        apply: "build", // Only run during build, not dev
        async closeBundle() {
            const outputDir = path.resolve(__dirname, "../../output/browser");
            const manifestPath = path.join(outputDir, ".vite/manifest.json");

            try {
                // Read the manifest file
                const manifestContent = await fs.promises.readFile(
                    manifestPath,
                    "utf-8",
                );
                const manifest = JSON.parse(manifestContent);

                console.log("\nProcessing manifest for entry points...");

                // Process each entry point
                for (const [entryKey, entryData] of Object.entries(
                    manifest,
                ) as [string, ManifestEntry][]) {
                    // Skip non-entry files (chunks, assets, etc.)
                    if (!entryData.isEntry) {
                        continue;
                    }

                    const entryName = path.basename(
                        entryKey,
                        path.extname(entryKey),
                    );
                    const mainFileName = entryData.file; // This will be X-main.js
                    const finalFileName = mainFileName.replace(
                        "-main.js",
                        ".js",
                    );

                    console.log(
                        `Creating ${finalFileName} from ${mainFileName}...`,
                    );

                    // Collect all dependencies for this entry point
                    const dependencies = new Set<string>();

                    // Add the main file itself
                    dependencies.add("./" + entryData.file);

                    // Add CSS files if any
                    if (entryData.css && entryData.css.length > 0) {
                        entryData.css.forEach((cssFile: string) => {
                            dependencies.add("./" + cssFile);
                        });
                    }

                    // Add dynamic imports/chunks if any
                    if (
                        entryData.dynamicImports &&
                        entryData.dynamicImports.length > 0
                    ) {
                        entryData.dynamicImports.forEach(
                            (dynamicImport: string) => {
                                dependencies.add("./" + dynamicImport);
                            },
                        );
                    }

                    // Add imports if any
                    if (entryData.imports && entryData.imports.length > 0) {
                        entryData.imports.forEach((importFile: string) => {
                            dependencies.add("./" + importFile);
                        });
                    }

                    // Create the final JS file content that imports all dependencies
                    let finalContent = "// Auto-generated entry point file\n";
                    finalContent += `// This file imports all dependencies for the ${entryName} bundle\n\n`;

                    // Import all dependencies
                    const sortedDependencies = Array.from(dependencies).sort();
                    sortedDependencies.forEach((dep) => {
                        if (dep.endsWith(".css")) {
                            // For CSS files, import them
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
                    await fs.promises.writeFile(
                        finalFilePath,
                        finalContent,
                        "utf-8",
                    );

                    console.log(`  ✓ Created ${finalFileName}`);
                }

                console.log("Post-build processing completed successfully!\n");
            } catch (error) {
                console.error("Error during post-build processing:", error);
                throw error;
            }
        },
    };
}

// Helper function to inject CSS into DOM
function createCssInjector() {
    return `
function injectCss(cssContent, source) {
    if (typeof window !== 'undefined' && window.document) {
        const style = document.createElement('style');
        style.setAttribute('data-source', source || 'inline');
        style.textContent = cssContent;
        document.head.appendChild(style);
    }
}`;
}

// Custom plugin to transform LESS imports to inline CSS injection (build only)
function transformLessImportsPlugin(): Plugin {
    return {
        name: "transform-less-imports",
        apply: "build", // Only apply during build, not dev
        transform(code, id) {
            // Only process TypeScript/JavaScript files
            if (!id.match(/\.(ts|tsx|js|jsx)$/)) {
                return null;
            }

            // Look for LESS import statements in relative paths
            const lessImportRegex = /import\s+['"](\.\/[^'"\n]*\.less)['"]/g;
            const matches = [...code.matchAll(lessImportRegex)];

            if (matches.length === 0) {
                return null;
            }

            let transformedCode = code;
            const injectedCss: string[] = [];

            matches.forEach((match, index) => {
                const lessPath = match[1];
                const variableName = `cssContent_${index}`;

                const originalImport = match[0];
                const newImport = `import ${variableName} from '${lessPath}?inline';`;
                transformedCode = transformedCode.replace(
                    originalImport,
                    newImport,
                );
                injectedCss.push(`injectCss(${variableName}, '${lessPath}');`);
            });

            const injectorFunction = createCssInjector();
            const immediateInjection = `
// Auto-inject CSS immediately when module loads
${injectedCss.map((call) => `(function() { ${call} })();`).join("\n")}
`;

            transformedCode = `${injectorFunction}\n${immediateInjection}\n${transformedCode}`;

            return { code: transformedCode, map: null };
        },
    };
}
// Use dynamic imports so that if Vite/esbuild emits a CommonJS wrapper for this
// config, Node can still load ESM-only plugins (like @vitejs/plugin-react) via
// native dynamic import instead of require().
export default defineConfig(async () => {
    // Define entry points to match webpack configuration
    const entryPoints: Record<string, string> = {
        // Special bundles that were previously built separately
        editablePageBundle: "./bookEdit/editablePage.ts",
        editTabBundle: "./bookEdit/editViewFrame.ts",
        spreadsheetBundle: "./spreadsheet/spreadsheetBundleRoot.ts",
        toolboxBundle: "./bookEdit/toolbox/toolboxBootstrap.ts",

        // Regular bundles
        readerSetupBundle:
            "./bookEdit/toolbox/readers/readerSetup/readerSetup.ts",
        bookPreviewBundle:
            "./collectionsTab/collectionsTabBookPane/bookPreview.ts",
        pageThumbnailListBundle:
            "./bookEdit/pageThumbnailList/pageThumbnailList.tsx",
        pageControlsBundle:
            "./bookEdit/pageThumbnailList/pageControls/pageControls.tsx",
        accessibilityCheckBundle: glob.sync(
            "./publish/accessibilityCheck/**/*.tsx",
        )[0], // Take first match
        subscriptionSettingsBundle: "./collection/subscriptionSettingsTab.tsx",
        performanceLogBundle: "./performance/PerformanceLogPage.tsx",
        appBundle: "./app/App.tsx",
        problemReportBundle: "./problemDialog/ProblemDialog.tsx",
        messageBoxBundle: "./utils/BloomMessageBox.tsx",
        bookMakingSettingsBundle: "./collection/bookMakingSettingsControl.tsx",
        progressDialogBundle: "./react_components/Progress/ProgressDialog.tsx",
        requiresSubscriptionBundle:
            "./react_components/requiresSubscription.tsx",
        createTeamCollectionDialogBundle:
            "./teamCollection/CreateTeamCollection.tsx",
        teamCollectionSettingsBundle:
            "./teamCollection/TeamCollectionSettingsPanel.tsx",
        joinTeamCollectionDialogBundle:
            "./teamCollection/JoinTeamCollectionDialog.tsx",
        autoUpdateSoftwareDlgBundle:
            "./react_components/AutoUpdateSoftwareDialog.tsx",
        duplicateManyDlgBundle: "./bookEdit/duplicateManyDialog.tsx",
        copyrightAndLicenseBundle:
            "./bookEdit/copyrightAndLicense/CopyrightAndLicenseDialog.tsx",
        collectionsTabPaneBundle: "./collectionsTab/CollectionsTabPane.tsx",
        publishTabPaneBundle: "./publish/PublishTab/PublishTabPane.tsx",
        languageChooserBundle: "./collection/LanguageChooserDialog.tsx",
        newCollectionLanguageChooserBundle:
            "./collection/NewCollectionLanguageChooser.tsx",
        registrationDialogBundle:
            "./react_components/registration/registrationDialog.tsx",
        editTopBarControlsBundle: "./bookEdit/topbar/editTopBarControls.tsx",
    };

    return {
        plugins: [
            react({
                reactRefreshHost: `http://localhost:${process.env.PORT || 5173}`,
                babel: {
                    parserOpts: {
                        // This enables decorators like @mobxReact.observer.
                        plugins: ["decorators-legacy"],
                    },
                },
            }),
            transformLessImportsPlugin(), // Transform LESS imports to inline CSS injection (build only)
            compilePugPlugin(), // Compile Pug files to HTML during build
            compileLessPlugin(), // Compile standalone LESS files to CSS during build
            compileMarkdownPlugin(), // Compile Markdown files to HTML during build
            postBuildPlugin(), // Process manifest and create final bundles (build only)
            // Copy files that need flattening (structured: false)
            viteStaticCopy({
                structured: false,
                targets: [
                    // Copy jQuery (equivalent to gulpfile's jquery.min.js copy with prefix: 3)
                    {
                        src: "node_modules/jquery/dist/jquery.min.js",
                        dest: ".",
                    },
                    // Copy bloom-player dist files (equivalent to gulpfile's nodeFilesNeededInOutput with prefix: 1)
                    {
                        src: "node_modules/bloom-player/dist/*",
                        dest: "./bloom-player/dist/",
                    },
                ],
            }),
            // Copy files preserving directory structure (structured: true)
            viteStaticCopy({
                structured: true,
                targets: [
                    // Copy all files except certain extensions (equivalent to gulpfile's filesThatMightBeNeededInOutput)
                    {
                        src: [
                            "**/*.*",
                            "!**/*.ts",
                            "!**/*.tsx",
                            "!**/*.pug",
                            "!**/*.md",
                            "!**/*.less",
                            "!**/*.bat",
                            "!**/node_modules/**/*.*",
                            "!**/tsconfig.json",
                        ],
                        dest: ".",
                    },
                ],
            }),
        ],
        server: {
            port: 5173,
            strictPort: true,
        },
        build: {
            outDir: "../../output/browser",
            sourcemap: true, // Generate source maps for debugging
            minify: false, // Disable minification for better debugging (optional)
            cssCodeSplit: false, // Inline CSS into JS bundles like webpack
            manifest: true, // Generate manifest.json for post-processing
            target: "esnext", // Ensure modern output for decorator support
            rollupOptions: {
                input: entryPoints,
                output: {
                    entryFileNames: "[name]-main.js", // Change to X-main.js format
                    chunkFileNames: "[name].js",
                    assetFileNames: "[name].[ext]",
                    // Prevent jQuery from being incorrectly re-exported from other modules
                    manualChunks: {
                        jquery: ["jquery"],
                        localizationManager: [
                            "lib/localizationManager/localizationManager",
                        ],
                    },
                },
            },
        },
        esbuild: {
            tsconfigRaw: {
                compilerOptions: {
                    experimentalDecorators: true,
                },
            },
        },
        resolve: {
            preserveSymlinks: false, // Ensure consistent file name resolution
            dedupe: [
                "react",
                "react-dom",
                "@emotion/react",
                "@emotion/styled",
                "@mui/base",
                "@mui/material",
                "@mui/system",
                "@mui/utils",
                "@mui/private-theming",
            ],
            alias: {
                "@": path.resolve(__dirname, "."),
                // Browser shims for Node built-ins used by some deps
                os: path.resolve(__dirname, "shims/os.ts"),
                // Module resolution paths to match webpack configuration
                errorHandler: path.resolve(__dirname, "lib/errorHandler.ts"),
                "jquery.hasAttr.js": path.resolve(
                    __dirname,
                    "bookEdit/js/jquery.hasAttr.js",
                ),
                "jquery.i18n.custom.ts": path.resolve(
                    __dirname,
                    "lib/jquery.i18n.custom.ts",
                ),
                "jasmine-jquery": path.resolve(
                    __dirname,
                    "typings/jasmine-jquery",
                ),
                "long-press/jquery.mousewheel.js": path.resolve(
                    __dirname,
                    "lib/long-press/jquery.mousewheel.js",
                ),
                "long-press/jquery.longpress.js": path.resolve(
                    __dirname,
                    "lib/long-press/jquery.longpress.js",
                ),
                "App.less": path.resolve(__dirname, "app/App.less"),
            },
        },
        test: {
            setupFiles: ["./vitest.setup.ts"],
            include: ["./**/*{test,spec,Spec}.{js,ts,jsx,tsx}"],
            // include: ["./bookEdit/toolbox/talkingBook/audioRecordingSpec.ts"], // Temporarily only run this file
            // include: ["./bookEdit/sourceBubbles/SourceBubblesSpec.ts"], // Temporarily only run this file
            // include: ["./bookEdit/toolbox/readers/readerSetup/readerSetupSpec.ts"], // Temporarily only run this file
            exclude: [
                "**/node_modules/**",
                "**/dist/**",
                "**/cypress/**",
                "**/.{idea,git,cache,output,temp}/**",
                "**/{karma,rollup,webpack,vite,vitest,jest,ava,babel,nyc,cypress,tsup,build}.config.*",
                "**/react_components/component-tester/**", // Exclude playwright component tests
                "**/*.uitest.{ts,tsx}", // Exclude UI tests that use Playwright
            ],
            environment: "jsdom",
            globals: false,
            testTimeout: 30000, // Increase timeout for async iframe setup
            sourcemap: true, // Enable source maps for debugging
            deps: {
                inline: ["vitest-canvas-mock"],
            },
            browser: {
                enabled: false, // Disabled due to xregexp module resolution issues
                provider: playwright(),
                instances: [
                    {
                        browser: "chromium",
                    },
                ],
            },
            environmentOptions: {
                jsdom: {
                    resources: "usable",
                },
            },
        },
        optimizeDeps: {
            include: ["jquery"],
            exclude: ["lib/localizationManager/localizationManager"],
        },
        define: {
            // Add global constants here if your webpack config defines any
            // Example: __DEV__: JSON.stringify(true)
            global: "globalThis", // Fix "global is not defined" error
        },
    };
    // ...other options as needed to match your webpack config...
});
