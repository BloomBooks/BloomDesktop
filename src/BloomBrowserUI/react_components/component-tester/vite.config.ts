/**
 * Vite configuration for the React component test harness.
 *
 * This configuration:
 * - Sets up a dev server on port 5173
 * - Mocks Bloom API endpoints (channel, error reporting)
 * - Enables React hot module reloading
 * - Serves component-harness.html at the root path
 *
 * Used by both:
 * - Manual testing: `yarn dev` then open http://127.0.0.1:5173
 * - Playwright tests: Automatically started by test runner
 *
 * Note:
 * - Test data is passed directly as props via setTestComponent() (see component-harness.tsx)
 * - Localization is handled by calling bypassLocalization() in test setup
 */

import { resolve } from "path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const respondWithJson = (
    res: import("node:http").ServerResponse,
    payload: unknown,
) => {
    res.statusCode = 200;
    res.setHeader("Content-Type", "application/json");
    res.end(JSON.stringify(payload));
};

export default defineConfig({
    root: __dirname,
    plugins: [
        react(),
        {
            name: "serve-component-harness",
            configureServer(server) {
                server.middlewares.use((req, res, next) => {
                    if (
                        req.url?.startsWith("/?") ||
                        req.url === "/" ||
                        req.url === "/index.html"
                    ) {
                        const query = req.url.includes("?")
                            ? req.url.substring(req.url.indexOf("?"))
                            : "";
                        req.url = "/component-harness.html" + query;
                    }
                    next();
                });
            },
        },
        {
            name: "component-tester-mock-api",
            configureServer(server) {
                server.middlewares.use((req, res, next) => {
                    if (!req.url) {
                        next();
                        return;
                    }

                    // GET endpoints
                    if (req.method === "GET") {
                        // Match with or without double slash (bloomApi adds trailing slash)
                        if (
                            req.url.startsWith("/bloom/api/common/channel") ||
                            req.url.startsWith("/bloom/api//common/channel")
                        ) {
                            respondWithJson(res, "developer");
                            return;
                        }
                        // Mock localization data to prevent 404 errors in console
                        if (
                            req.url.startsWith("/bloom/api/i18n/") ||
                            req.url.startsWith("/bloom/api//i18n/")
                        ) {
                            // Return empty localization data
                            respondWithJson(res, {});
                            return;
                        }
                    }

                    // POST endpoints - log errors for visibility during testing
                    if (req.method === "POST") {
                        if (
                            req.url.startsWith(
                                "/bloom/api/common/preliminaryError",
                            ) ||
                            req.url.startsWith("/bloom/api/common/error")
                        ) {
                            let body = "";
                            req.on("data", (chunk) => {
                                body += chunk.toString();
                            });
                            req.on("end", () => {
                                console.error(
                                    "Error reported from component:",
                                    body,
                                );
                                respondWithJson(res, { success: true });
                            });
                            return;
                        }
                    }

                    next();
                });
            },
        },
    ],
    server: {
        host: "127.0.0.1",
        port: 5173,
        open: false,
        strictPort: true,
        fs: {
            allow: ["..", "../.."],
        },
    },
    preview: {
        host: "127.0.0.1",
        port: 4173,
    },
    resolve: {
        alias: {
            "@component-tester": resolve(__dirname),
        },
    },
    build: {
        rollupOptions: {
            input: resolve(__dirname, "component-harness.html"),
        },
        outDir: "dist",
        emptyOutDir: true,
        sourcemap: true,
    },
});
