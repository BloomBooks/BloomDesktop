import { defineConfig } from "vite";
import type { Plugin } from "vite";
import react from "@vitejs/plugin-react";
import * as path from "path";

// This config builds ONLY the editablePage bundle as a single self-contained JS file.
// Usage: yarn vite build --config vite.editablePage.config.ts
export default defineConfig(() => {
    const entry = "./bookEdit/editablePage.ts";

    // Helper function to inject CSS into DOM (mirrors main vite.config.ts)
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
            enforce: "pre",
            apply: "build",
            transform(code, id) {
                // Only process TypeScript/JavaScript files
                if (!id.match(/\.(ts|tsx|js|jsx)$/)) {
                    return null;
                }

                // Look for LESS import statements in relative paths
                const lessImportRegex =
                    /import\s+['"](\.\/[^'"\n]*\.less)['"]/g;
                const matches: RegExpMatchArray[] = [];
                let m: RegExpExecArray | null;
                while ((m = lessImportRegex.exec(code)) !== null) {
                    if (m.index === lessImportRegex.lastIndex) {
                        lessImportRegex.lastIndex++;
                    }
                    matches.push(m as unknown as RegExpMatchArray);
                }

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
                    injectedCss.push(
                        `injectCss(${variableName}, '${lessPath}');`,
                    );
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

    return {
        plugins: [
            react({
                reactRefreshHost: `http://localhost:${process.env.PORT || 5173}`,
            }),
            transformLessImportsPlugin(),
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
