import { defineConfig } from "vite";
import type { Plugin } from "vite";
import * as path from "path";
import { glob } from "glob";
import react from "@vitejs/plugin-react";
import { viteStaticCopy } from "vite-plugin-static-copy";

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

// Custom plugin to fix jQuery import issues
// I'm committing this temporarily so I can go work on other stuff, but I think it's a mistake.
// It's a bandaid to fix a fundamental problem with how Vite is handling our dependencies and
// transpiling. We may not need this plugin at all; if we do, it probably needs to be more general.
function fixJqueryImportsPlugin(): Plugin {
    return {
        name: "fix-jquery-imports",
        apply: "build",
        generateBundle(options, bundle) {
            // Post-process the audioRecording bundle to fix jQuery imports
            Object.keys(bundle).forEach((fileName) => {
                const chunk = bundle[fileName];
                if (
                    chunk.type === "chunk" &&
                    fileName === "audioRecording.js"
                ) {
                    // Fix the incorrect import statement that imports $ from localizationManager
                    const originalCode = chunk.code;

                    // Check if we need to preserve theOneLocalizationManager import
                    const needsLocalizationManager = originalCode.includes(
                        "theOneLocalizationManager",
                    );

                    // Remove the problematic import line
                    chunk.code = chunk.code.replace(
                        /import\s*{\s*\$\s*as\s*\$\$1[^}]*}\s*from\s*"\.\/localizationManager\.js";\s*/g,
                        "",
                    );

                    // If we need localizationManager, add a proper import
                    if (
                        needsLocalizationManager &&
                        !chunk.code.includes('from "./localizationManager.js"')
                    ) {
                        chunk.code =
                            'import { t as theOneLocalizationManager } from "./localizationManager.js";\n' +
                            chunk.code;
                    }

                    // Clean up any duplicate jQuery imports
                    const jqueryImportRegex =
                        /import[^;]*from\s*"\.\/jquery\.js";\s*/g;
                    const jqueryImports = chunk.code.match(jqueryImportRegex);
                    if (jqueryImports && jqueryImports.length > 1) {
                        // Remove all jQuery imports first
                        chunk.code = chunk.code.replace(jqueryImportRegex, "");
                        // Add a single clean jQuery import at the top
                        chunk.code =
                            'import $ from "./jquery.js";\n' + chunk.code;
                    }
                }
            });
        },
    };
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

            // Look for LESS import statements
            const lessImportRegex = /import\s+['"](\.\/[^'"]*\.less)['"]/g;
            const matches = [...code.matchAll(lessImportRegex)];

            if (matches.length === 0) {
                return null;
            }

            let transformedCode = code;
            const injectedCss: string[] = [];

            // Replace each LESS import with inline CSS import and injection
            matches.forEach((match, index) => {
                const lessPath = match[1];
                const variableName = `cssContent_${index}`;

                // Replace the import statement
                const originalImport = match[0];
                const newImport = `import ${variableName} from '${lessPath}?inline';`;

                transformedCode = transformedCode.replace(
                    originalImport,
                    newImport,
                );
                injectedCss.push(`injectCss(${variableName}, '${lessPath}');`);
            });

            // Add the CSS injector function and immediate injection at the top of the file
            const injectorFunction = createCssInjector();
            const immediateInjection = `
// Auto-inject CSS immediately when module loads
${injectedCss.map((call) => `(function() { ${call} })();`).join("\n")}
`;

            transformedCode = `${injectorFunction}\n${immediateInjection}\n${transformedCode}`;

            return {
                code: transformedCode,
                map: null, // You could generate a source map here if needed
            };
        },
    };
}
// Use dynamic imports so that if Vite/esbuild emits a CommonJS wrapper for this
// config, Node can still load ESM-only plugins (like @vitejs/plugin-react) via
// native dynamic import instead of require().
export default defineConfig(async () => {
    // Define entry points to match webpack configuration
    const entryPoints = {
        editTabBundle: "./bookEdit/editViewFrame.ts",
        readerSetupBundle:
            "./bookEdit/toolbox/readers/readerSetup/readerSetup.ts",
        editablePageBundle: "./bookEdit/editablePage.ts",
        bookPreviewBundle:
            "./collectionsTab/collectionsTabBookPane/bookPreview.ts",
        toolboxBundle: "./bookEdit/toolbox/toolboxBootstrap.ts",
        spreadsheetBundle: "./spreadsheet/spreadsheetBundleRoot.ts",
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
            }),
            fixJqueryImportsPlugin(), // Fix jQuery import issues in audioRecording.js
            transformLessImportsPlugin(), // Transform LESS imports to inline CSS injection (build only)
            //pugPlugin()
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
            rollupOptions: {
                input: entryPoints,
                external: [
                    // External Node.js modules that shouldn't be bundled for browser
                    "os",
                    "path",
                    "fs",
                    "crypto",
                ],
                output: {
                    entryFileNames: "[name]-main.js", // Change to X-main.js format
                    chunkFileNames: "[name].js",
                    assetFileNames: "[name].[ext]",
                    // Prevent jQuery from being incorrectly re-exported from other modules
                    manualChunks: {
                        jquery: ["jquery"],
                    },
                },
            },
        },
        resolve: {
            preserveSymlinks: false, // Ensure consistent file name resolution
            alias: {
                "@": path.resolve(__dirname, "."),
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
        },
        define: {
            // Add global constants here if your webpack config defines any
            // Example: __DEV__: JSON.stringify(true)
            global: "globalThis", // Fix "global is not defined" error
        },
    };
    // ...other options as needed to match your webpack config...
});
