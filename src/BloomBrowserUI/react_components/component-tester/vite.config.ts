/**
 * Vite configuration for the React component test harness.
 *
 * This configuration:
 * - Sets up a dev server on port 5183
 * - Mocks Bloom API endpoints (channel, error reporting)
 * - Enables React hot module reloading
 * - Serves component-harness.html at the root path
 *
 * Used by both:
 * - Manual testing: `yarn dev` then open http://127.0.0.1:5183
 * - Playwright tests: Automatically started by test runner
 *
 * Note:
 * - Test data is passed directly as props via setTestComponent() (see component-harness.tsx)
 * - Localization is handled by calling bypassLocalization() in test setup
 */

import { createReadStream, existsSync } from "node:fs";
import { spawn } from "node:child_process";
import { resolve } from "path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const useMocks = process.env.BLOOM_COMPONENT_TESTER_USE_BACKEND !== "1";

const assetRedirects: Array<{
    requestPath: string;
    filePath: string;
}> = [
    {
        requestPath: "/bloom/bookEdit/pageThumbnailList/pageThumbnailList.css",
        filePath: resolve(
            __dirname,
            "../../../../output/browser/bookEdit/pageThumbnailList/pageThumbnailList.css",
        ),
    },
    {
        requestPath: "/bloom/bookLayout/basePage.css",
        filePath: resolve(
            __dirname,
            "../../../../output/browser/bookLayout/basePage.css",
        ),
    },
    {
        requestPath: "/bloom/bookLayout/previewMode.css",
        filePath: resolve(
            __dirname,
            "../../../../output/browser/collectionsTab/collectionsTabBookPane/previewMode.css",
        ),
    },
];

const tryServeStaticAsset = (
    req: import("node:http").IncomingMessage,
    res: import("node:http").ServerResponse,
) => {
    if (!useMocks || req.method !== "GET" || !req.url) {
        return false;
    }

    let pathname = req.url;
    try {
        pathname = new URL(req.url, "http://localhost").pathname;
    } catch {
        const queryIndex = req.url.indexOf("?");
        pathname = queryIndex >= 0 ? req.url.substring(0, queryIndex) : req.url;
    }

    const redirect = assetRedirects.find(
        (entry) => entry.requestPath === pathname,
    );
    if (!redirect) {
        return false;
    }

    if (!existsSync(redirect.filePath)) {
        res.statusCode = 404;
        res.end("Not Found");
        return true;
    }

    res.statusCode = 200;
    res.setHeader("Content-Type", "text/css");
    const stream = createReadStream(redirect.filePath);
    stream.on("error", () => {
        res.statusCode = 500;
        res.end("Failed to read asset");
    });
    stream.pipe(res);
    return true;
};

const launchBrowser = (targetUrl: string) => {
    const platform = process.platform;
    let command: string;
    let args: string[];

    if (platform === "win32") {
        command = "cmd";
        args = ["/c", "start", "", targetUrl];
    } else if (platform === "darwin") {
        command = "open";
        args = [targetUrl];
    } else {
        command = "xdg-open";
        args = [targetUrl];
    }

    try {
        const child = spawn(command, args, {
            stdio: "ignore",
            detached: true,
        });
        child.on("error", (error) => {
            console.error(
                "Component tester mock: failed to open browser",
                error,
            );
        });
        child.unref();
    } catch (error) {
        console.error("Component tester mock: failed to open browser", error);
    }
};

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
            transformIndexHtml() {
                // Inject backend availability flag into the page
                return [
                    {
                        tag: "script",
                        injectTo: "head-prepend",
                        children: `window.__BLOOM_HAS_BACKEND__ = ${!useMocks};`,
                    },
                ];
            },
        },
        {
            name: "component-tester-mock-api",
            configureServer(server) {
                server.middlewares.use((req, res, next) => {
                    if (!useMocks) {
                        next();
                        return;
                    }
                    if (!req.url) {
                        next();
                        return;
                    }

                    if (tryServeStaticAsset(req, res)) {
                        return;
                    }

                    if (
                        useMocks &&
                        req.method === "GET" &&
                        req.url.startsWith("/bloom/api/collections/bookFile")
                    ) {
                        handleMockBookFileRequest(req, res);
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
                            req.url.startsWith("/bloom/api/common/openUrl") ||
                            req.url.startsWith("/bloom/api//common/openUrl")
                        ) {
                            let targetUrl = "";
                            try {
                                const parsed = new URL(
                                    req.url,
                                    "http://localhost",
                                );
                                targetUrl =
                                    parsed.searchParams.get("url") ?? "";
                            } catch {
                                // Leave targetUrl as empty
                            }
                            if (targetUrl) {
                                const suppressOpen =
                                    process.env
                                        .BLOOM_COMPONENT_TESTER_SUPPRESS_OPEN ===
                                    "1";
                                if (suppressOpen) {
                                    console.info(
                                        `Component tester mock: would open browser to ${targetUrl}`,
                                    );
                                } else {
                                    launchBrowser(targetUrl);
                                }
                            }
                            respondWithJson(res, { success: true });
                            return;
                        }
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
    optimizeDeps: {
        exclude: [
            "playwright-core",
            "chromium-bidi",
            "chromium-bidi/lib/cjs/bidiMapper/BidiMapper",
            "chromium-bidi/lib/cjs/cdp/CdpConnection",
        ],
    },
    server: {
        host: "127.0.0.1",
        port: 5183,
        open: false,
        strictPort: true,
        fs: {
            allow: ["..", "../.."],
        },
        proxy: useMocks
            ? undefined
            : {
                  "/bloom": {
                      target: "http://localhost:8089",
                      changeOrigin: true,
                      secure: false,
                  },
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
            external: [
                "chromium-bidi/lib/cjs/bidiMapper/BidiMapper",
                "chromium-bidi/lib/cjs/cdp/CdpConnection",
            ],
        },
        outDir: "dist",
        emptyOutDir: true,
        sourcemap: true,
    },
});

const handleMockBookFileRequest = (
    req: import("node:http").IncomingMessage,
    res: import("node:http").ServerResponse,
) => {
    // Expected format: /bloom/api/collections/bookFile?book-id={bookId}&file={relativePath}
    try {
        const url = new URL(req.url!, "http://localhost");
        if (url.pathname !== "/bloom/api/collections/bookFile") {
            res.statusCode = 404;
            res.end("Not Found");
            return;
        }

        const fileParam = url.searchParams.get("file");
        if (!fileParam) {
            res.statusCode = 400;
            res.end("Missing file parameter");
            return;
        }

        const requestedPath = fileParam.replace(/^[\\/]+/, "");

        if (requestedPath === "appearance.css") {
            const appearancePath = resolve(
                __dirname,
                "../../../../output/browser/appearanceThemes/appearance-theme-default.css",
            );
            if (!existsSync(appearancePath)) {
                res.statusCode = 404;
                res.end("Not Found");
                return;
            }
            res.statusCode = 200;
            res.setHeader("Content-Type", "text/css");
            const stream = createReadStream(appearancePath);
            stream.on("error", () => {
                res.statusCode = 500;
                res.end("Failed to read asset");
            });
            stream.pipe(res);
            return;
        }
    } catch {
        // fall through
    }

    res.statusCode = 404;
    res.end("Not Found");
};
