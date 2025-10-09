import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import * as path from "path";

// Standalone build for the Spreadsheet bundle only.
// Usage: yarn vite build --config vite.spreadsheet.config.ts
export default defineConfig(() => {
    const entry = "./spreadsheet/spreadsheetBundleRoot.ts";

    return {
        plugins: [
            react({
                reactRefreshHost: `http://localhost:${process.env.PORT || 5173}`,
            }),
        ],
        build: {
            outDir: "../../output/browser",
            sourcemap: true,
            minify: false,
            cssCodeSplit: false,
            manifest: false,
            rollupOptions: {
                input: { spreadsheetBundle: entry },
                output: {
                    entryFileNames: "spreadsheetBundle.js",
                    chunkFileNames: "spreadsheetBundle-[name].js",
                    assetFileNames: "[name].[ext]",
                    inlineDynamicImports: true,
                },
            },
        },
        resolve: {
            preserveSymlinks: false,
            alias: {
                "@": path.resolve(__dirname, "."),
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
        define: { global: "globalThis" },
        optimizeDeps: {
            include: ["jquery"],
            exclude: ["lib/localizationManager/localizationManager"],
        },
    };
});
