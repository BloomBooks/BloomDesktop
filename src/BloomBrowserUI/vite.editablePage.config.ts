import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import * as path from "path";

// This config builds ONLY the editablePage bundle as a single self-contained JS file.
// Usage: yarn vite build --config vite.editablePage.config.ts
export default defineConfig(() => {
    const entry = "./bookEdit/editablePage.ts";

    return {
        plugins: [
            react({
                reactRefreshHost: `http://localhost:${process.env.PORT || 5173}`,
            }),
            // We still copy only the minimal shared static assets needed by editablePage.
            // If this set is insufficient, extend targets similarly to main config.
        ],
        build: {
            outDir: "../../output/browser",
            sourcemap: true,
            minify: false,
            cssCodeSplit: false,
            manifest: false, // single bundle build does not need manifest
            rollupOptions: {
                input: { editablePageBundle: entry },
                // Force a single bundle by avoiding extra manual chunks
                output: {
                    entryFileNames: "editablePageBundle.js",
                    chunkFileNames: "editablePageBundle-[name].js",
                    assetFileNames: "[name].[ext]",
                    inlineDynamicImports: true, // Merge dynamic imports into one file
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
        define: {
            global: "globalThis",
        },
        optimizeDeps: {
            include: ["jquery"],
            exclude: ["lib/localizationManager/localizationManager"],
        },
    };
});
