// Vite Configuration for Bloom Browser UI
// Vite is a modern build tool that replaces webpack for development and production builds.
// Key concepts:
// - Plugins: Extend Vite's capabilities (similar to webpack loaders/plugins)
// - Dev Server: Hot module reload during development
// - Build: Optimized production bundles using Rollup
// - Entry Points: Starting files for each bundle (like webpack's entry config)

import { defineConfig } from "vite";
import type { Plugin } from "vite";
import * as path from "path";
import { glob } from "glob";
import react from "@vitejs/plugin-react";
import { viteStaticCopy } from "vite-plugin-static-copy";
import * as fs from "fs";
import less from "less";
import MarkdownIt from "markdown-it";
import markdownItContainer from "markdown-it-container";
import markdownItAttrs from "markdown-it-attrs";
import { playwright } from "@vitest/browser-playwright";
import { compilePugFiles } from "./scripts/compilePug.mjs";

// Custom plugin to compile Pug files to HTML
// There are a couple of npm packages for pug, but as of October 2025, they are experimental
// and/or aimed at dynamically serving pug, like vite dev does with js. For now, we're good
// with a simple static compilation during build. Claude sonnet 4.5 came up with this.
// Note that it also builds pug files from ../content. This is because we haven't yet
// changed content to use vite.
function compilePugPlugin(): Plugin {
    return {
        name: "compile-pug",
        apply: "build",
        async closeBundle() {
            await compilePugFiles();
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
                            // Check if this import is an entry point in the manifest
                            const importEntry = manifest[importFile];
                            if (importEntry && importEntry.isEntry) {
                                // This is an entry point - import the corresponding bundle file
                                // Convert the -main.js to .js (e.g., copyrightAndLicenseBundle-main.js -> copyrightAndLicenseBundle.js)
                                const importBundleFile =
                                    importEntry.file.replace("-main.js", ".js");
                                console.log(
                                    `  Resolving entry point dependency: ${importFile} -> ${importBundleFile}`,
                                );
                                dependencies.add("./" + importBundleFile);

                                // Also add the CSS from that entry point if it has any
                                if (
                                    importEntry.css &&
                                    importEntry.css.length > 0
                                ) {
                                    importEntry.css.forEach(
                                        (cssFile: string) => {
                                            dependencies.add("./" + cssFile);
                                        },
                                    );
                                }
                            } else {
                                // Not an entry point - use as-is
                                dependencies.add("./" + importFile);
                            }
                        });
                    }

                    // Create the final JS file content that imports all dependencies
                    let finalContent = "// Auto-generated entry point file\n";
                    finalContent += `// This file imports all dependencies for the ${entryName} bundle\n\n`;

                    // Separate CSS and JS dependencies
                    const cssDependencies: string[] = [];
                    const jsDependencies: string[] = [];

                    const sortedDependencies = Array.from(dependencies).sort();
                    sortedDependencies.forEach((dep) => {
                        if (dep.endsWith(".css")) {
                            cssDependencies.push(dep);
                        } else if (dep.endsWith(".js")) {
                            jsDependencies.push(dep);
                        }
                    });

                    // Add CSS loading function if there are CSS dependencies
                    if (cssDependencies.length > 0) {
                        finalContent += `// Function to load CSS files dynamically\n`;
                        finalContent += `function loadCSS(href) {\n`;
                        finalContent += `    const link = document.createElement('link');\n`;
                        finalContent += `    link.rel = 'stylesheet';\n`;
                        finalContent += `    link.href = href;\n`;
                        finalContent += `    document.head.appendChild(link);\n`;
                        finalContent += `}\n\n`;

                        finalContent += `// Load CSS dependencies\n`;
                        cssDependencies.forEach((cssFile) => {
                            finalContent += `loadCSS('${cssFile}');\n`;
                        });
                        finalContent += `\n`;
                    }

                    // Import JS dependencies
                    if (jsDependencies.length > 0) {
                        finalContent += `// Import JS dependencies\n`;
                        jsDependencies.forEach((dep) => {
                            // Remove underscore prefix from dependency path if present
                            const cleanDep = dep.startsWith("./_")
                                ? "./" + dep.substring(3)
                                : dep;
                            finalContent += `import '${cleanDep}';\n`;
                        });
                    }

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

// Minimal build error reporter to surface early failures before manifest generation
function reportBuildErrorPlugin(): Plugin {
    return {
        name: "report-build-error",
        apply: "build",
        buildEnd(error) {
            if (error) {
                console.error(
                    "Vite build halted before manifest generation:",
                    error,
                );
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
export default defineConfig(async ({ command }) => {
    // ENTRY POINTS CONFIGURATION
    // Define all JavaScript/TypeScript entry points - these are the "root" files that
    // Vite will build into separate bundles. Each entry becomes a standalone .js file
    // that can be loaded independently by HTML pages.
    // This replaces webpack's entry configuration.
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

    // MAIN VITE CONFIGURATION
    // https://vitejs.dev/config/
    return {
        // PLUGINS: Extend Vite's functionality (runs during build and/or dev)
        plugins: [
            // React plugin: Enables JSX, Fast Refresh, and React-specific optimizations
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
            compilePugPlugin(), // Compile Pug templates to HTML during build
            compileLessPlugin(), // Compile standalone LESS files to CSS during build
            compileMarkdownPlugin(), // Compile Markdown files to HTML during build
            reportBuildErrorPlugin(),
            postBuildPlugin(), // Process manifest and create final bundles (build only)

            // STATIC FILE COPYING (BUILD ONLY)
            // vite-plugin-static-copy copies files from source to output directory
            // CRITICAL: These plugins must only run during build, not dev mode
            // In dev mode, scanning 525+ files causes 30+ second delays
            // Conditionally include these plugins only when command === 'build'
            ...(command === "build"
                ? [
                      // structured: false = flatten directory structure (all files go to dest root)
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
                      // structured: true = preserve directory structure when copying
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
                  ]
                : []),
        ],

        // DEV SERVER CONFIGURATION
        // Controls the local development server behavior
        server: {
            port: 5173, // Default Vite port
            strictPort: true, // Fail if port is already in use (don't try other ports)
            hmr: {
                protocol: "ws",
                host: "localhost", // The host where your Vite server is running
                port: 5173, // The port where your Vite server is running
                overlay: true,
            },
        },

        // BUILD CONFIGURATION
        // Controls how Vite creates production bundles
        build: {
            outDir: "../../output/browser", // Where to output built files
            sourcemap: true, // Generate .map files for debugging production code
            minify: false, // Keep code readable (set to 'esbuild' or 'terser' to minify)
            cssCodeSplit: true, // Generate separate CSS files (loaded dynamically by postBuildPlugin)
            manifest: true, // Generate manifest.json listing all build outputs
            target: "esnext", // Use latest JavaScript features (decorators, etc.)
            // Tell Vite's CommonJS plugin which exports comicaljs provides
            commonjsOptions: {
                include: [/node_modules/],
                transformMixedEsModules: true,
                // Explicitly declare named exports from CommonJS modules
                namedExports: {
                    comicaljs: [
                        "Bubble",
                        "Comical",
                        "BubbleSpec",
                        "BubbleSpecPattern",
                        "TailSpec",
                    ],
                },
            },

            // ROLLUP OPTIONS
            // Vite uses Rollup for production builds - these options configure Rollup
            rollupOptions: {
                input: entryPoints, // Which files to build (our entry points defined above)
                output: {
                    // File naming patterns for build outputs:
                    // [name] = the entry point key name (e.g., "editablePageBundle")
                    entryFileNames: "[name]-main.js", // Main entry files become X-main.js
                    chunkFileNames: "[name].js", // Shared code chunks
                    assetFileNames: "[name].[ext]", // CSS, images, etc.

                    // MANUAL CHUNKS
                    // Force certain modules into separate files instead of bundling with entries
                    // This prevents jQuery from being duplicated across bundles
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

        // ESBUILD CONFIGURATION
        // Vite uses esbuild for fast TypeScript transpilation
        esbuild: {
            tsconfigRaw: {
                compilerOptions: {
                    experimentalDecorators: true, // Enable @decorator syntax for MobX
                },
            },
        },

        // MODULE RESOLUTION CONFIGURATION
        // Controls how Vite finds and loads modules
        resolve: {
            preserveSymlinks: false, // Follow symlinks to actual files

            // DEDUPE: Prevent duplicate copies of these packages in bundles
            // If multiple dependencies use React, only include one copy
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

            // ALIAS: Path shortcuts for imports
            // Instead of: import x from "../../../lib/errorHandler"
            // You can use: import x from "errorHandler"
            // This matches our webpack configuration
            alias: {
                "@": path.resolve(__dirname, "."), // @ = project root
                // Browser shims for Node built-ins used by some dependencies
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

        // VITEST CONFIGURATION
        // Vitest is Vite's test runner (replaces Karma + Jasmine)
        // See vitest.dev for full documentation
        test: {
            setupFiles: ["./vitest.setup.ts"], // Run this file before each test file
            include: ["./**/*{test,spec,Spec}.{js,ts,jsx,tsx}"], // Which files are tests
            reporters: globalThis.process?.env?.TEAMCITY_VERSION
                ? ["default", "junit"]
                : ["default"],
            outputFile: "./bloombrowserui-test-results.xml",
            includeConsoleOutput: true,
            // Uncomment to run only specific test files during development:
            // include: ["./bookEdit/toolbox/talkingBook/audioRecordingSpec.ts"],
            exclude: [
                "**/node_modules/**",
                "**/dist/**",
                "**/cypress/**",
                "**/.{idea,git,cache,output,temp}/**",
                "**/{karma,rollup,webpack,vite,vitest,jest,ava,babel,nyc,cypress,tsup,build}.config.*",
                "**/react_components/component-tester/**", // Exclude playwright component tests
                "**/*.uitest.{ts,tsx}", // Exclude UI tests that use Playwright
            ],
            environment: "jsdom", // Use jsdom to simulate browser DOM in Node
            globals: false, // Don't inject global test functions (use imports instead)
            testTimeout: 30000, // 30 second timeout for async operations
            sourcemap: true, // Enable source maps for debugging test code
            deps: {
                inline: ["vitest-canvas-mock"], // Force this dep to be bundled (not externalized)
            },
            browser: {
                // This whole block is unused since enabled is false. The settings are our current
                // best guess for our next attempt to get browser mode working.
                enabled: false, // Browser mode disabled (we use jsdom instead)
                provider: playwright(),
                instances: [
                    {
                        browser: "chromium",
                    },
                ],
            },
            environmentOptions: {
                jsdom: {
                    resources: "usable", // Allow jsdom to load external resources
                },
            },
        },

        // DEPENDENCY OPTIMIZATION
        // Controls how Vite pre-bundles dependencies for faster dev server startup
        optimizeDeps: {
            include: [
                "jquery", // Always pre-bundle jQuery
                "comicaljs", // Pre-bundle comicaljs (webpack UMD bundle needs processing)
            ],
            exclude: ["lib/localizationManager/localizationManager"], // Don't pre-bundle this
            // Force Vite to treat comicaljs as having named exports even though it's CommonJS/UMD
            esbuildOptions: {
                plugins: [],
            },
        },

        // GLOBAL DEFINITIONS
        // Define global constants that get replaced at build time
        define: {
            global: "globalThis", // Polyfill for Node's "global" variable in browser
        },
    };
    // For more Vite configuration options, see: https://vitejs.dev/config/
});
