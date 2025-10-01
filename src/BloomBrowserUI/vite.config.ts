import { defineConfig } from "vite";
import * as path from "path";
import { glob } from "glob";
import react from "@vitejs/plugin-react";
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
            //pugPlugin()
        ],
        server: {
            port: 5173,
            strictPort: true,
        },
        build: {
            outDir: "../../output/browser",
            rollupOptions: {
                input: entryPoints,
                external: [
                    "react",
                    "react-dom",
                    // External Node.js modules that shouldn't be bundled for browser
                    "os",
                    "path",
                    "fs",
                    "crypto",
                ],
                output: {
                    entryFileNames: "[name].js",
                    chunkFileNames: "[name].js",
                    assetFileNames: "[name].[ext]",
                },
            },
        },
        resolve: {
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
        define: {
            // Add global constants here if your webpack config defines any
            // Example: __DEV__: JSON.stringify(true)
        },
    };
    // ...other options as needed to match your webpack config...
});
