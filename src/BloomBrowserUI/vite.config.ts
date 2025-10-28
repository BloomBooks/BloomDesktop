import { defineConfig } from "vite";
import type { Plugin } from "vite";
import * as path from "path";
import { glob } from "glob";
import react from "@vitejs/plugin-react";
import { viteStaticCopy } from "vite-plugin-static-copy";
import * as fs from "fs";
import pug from "pug";

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
        registrationDialogBundle: "./react_components/registrationDialog.tsx",
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
            viteStaticCopy({
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
                    // Copy other necessary files preserving directory structure
                    // Using individual patterns to maintain structure
                    {
                        src: "images/**/*",
                        dest: "images",
                    },
                    {
                        src: "fonts/**/*",
                        dest: "fonts",
                    },
                    {
                        src: "sounds/**/*",
                        dest: "sounds",
                    },
                    {
                        src: "help/**/*",
                        dest: "help",
                    },
                    {
                        // Be careful here. Changing it to lib/ double asterisk caused a weird bug
                        // where running in yarn dev mode failed to load the root file,
                        // complaining that there was an unexpected colon.
                        src: "bookEdit/**/*.{js,css,html,svg,png,jpg,gif,woff,woff2,ttf,eot}",
                        dest: "bookEdit",
                    },
                    {
                        src: "bookLayout/**/*",
                        dest: "bookLayout",
                    },
                    {
                        src: "lib/**/*.{js,css,html,svg,png,jpg,gif,woff,woff2,ttf,eot}",
                        dest: "lib",
                    },
                    {
                        src: "modified_libraries/**/*",
                        dest: "modified_libraries",
                    },
                    {
                        src: "themes/**/*",
                        dest: "themes",
                    },
                    {
                        src: "spreadsheet/*.html",
                        dest: "spreadsheet",
                    },
                    // Copy root-level non-compiled files
                    {
                        src: [
                            "*.{js,css,html,svg,png,jpg,gif,ico,json}",
                            "!*.ts",
                            "!*.tsx",
                            "!*.pug",
                            "!*.md",
                            "!*.less",
                        ],
                        dest: ".",
                    },
                    // Copy legacy commonBundle.js for backward compatibility
                    {
                        src: "legacy/commonBundle.js",
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
            // various things copilot suggested to match vite config to webpack.
            // For now, they didn't help and some broke things.
            environment: "jsdom",
            globals: false,
            // Uncomment and adjust as needed to match your test file patterns:
            // include: ["./src/**/talkingBookSpec.ts"],
            // deps: {
            //     inline: [
            //         // Add packages here that should not be externalized
            //         "xregexp"
            //     ]
            // }
            deps: {
                inline: [
                    "vitest-canvas-mock",
                    //"bookEdit/toolbox/readers/libSynphony/synphony_lib.js"
                ],
            },
            browser: {
                enabled: true,
                name: "chromium",
                //provider: "playwright"
            },
            // For this config, check https://github.com/vitest-dev/vitest/issues/740
            //threads: false,
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
