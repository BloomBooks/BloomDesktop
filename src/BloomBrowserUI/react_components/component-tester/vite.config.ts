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
import { resolve, normalize } from "path";
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

// Copilot wrote this when my show.sh script was failing to load images.
// I didn't debug why tryServeStaticAsset wasn't sufficient.
const tryServeBloomImageAsset = (
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

    if (!pathname.startsWith("/bloom/images/")) {
        return false;
    }

    const relativePath = decodeURIComponent(
        pathname.substring("/bloom/images/".length),
    );
    // Fail fast on suspicious paths.
    if (
        !relativePath ||
        relativePath.includes("..") ||
        relativePath.includes("\\")
    ) {
        res.statusCode = 400;
        res.end("Bad Request");
        return true;
    }

    const imagesRoot = resolve(__dirname, "../../images");
    const filePath = resolve(imagesRoot, relativePath);
    const normalizedImagesRoot = normalize(imagesRoot + "/");
    const normalizedFilePath = normalize(filePath);
    if (!normalizedFilePath.startsWith(normalizedImagesRoot)) {
        res.statusCode = 400;
        res.end("Bad Request");
        return true;
    }

    if (!existsSync(filePath)) {
        res.statusCode = 404;
        res.end("Not Found");
        return true;
    }

    const lowerPath = filePath.toLowerCase();
    if (lowerPath.endsWith(".png")) {
        res.setHeader("Content-Type", "image/png");
    } else if (lowerPath.endsWith(".svg")) {
        res.setHeader("Content-Type", "image/svg+xml");
    } else if (lowerPath.endsWith(".jpg") || lowerPath.endsWith(".jpeg")) {
        res.setHeader("Content-Type", "image/jpeg");
    } else if (lowerPath.endsWith(".gif")) {
        res.setHeader("Content-Type", "image/gif");
    } else {
        res.setHeader("Content-Type", "application/octet-stream");
    }

    res.statusCode = 200;
    const stream = createReadStream(filePath);
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
                        children: `window._SKIP_WEBSOCKET_CREATION_ = ${useMocks};`,
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

                    if (tryServeBloomImageAsset(req, res)) {
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

                    // mock GET endpoints
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

                    // mock POST endpoints
                    if (req.method === "POST") {
                        if (
                            req.url.startsWith("/bloom/api/link") ||
                            req.url.startsWith("/bloom/api//link")
                        ) {
                            let body = "";
                            let responded = false;
                            const respondOnce = (payload: unknown) => {
                                if (!responded) {
                                    responded = true;
                                    respondWithJson(res, payload);
                                }
                            };

                            req.on("data", (chunk) => {
                                body += chunk.toString();
                            });

                            req.on("end", () => {
                                const encodedTarget = body.trim();
                                let targetUrl = "";
                                if (encodedTarget) {
                                    try {
                                        targetUrl =
                                            decodeURIComponent(encodedTarget);
                                    } catch {
                                        targetUrl = encodedTarget;
                                    }
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

                                respondOnce({ success: true });
                            });

                            req.on("error", (error) => {
                                console.error(
                                    "Component tester mock: failed to read link request",
                                    error,
                                );
                                respondOnce({ success: false });
                            });

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
        // These MUI packages live under src/BloomBrowserUI/node_modules instead of this
        // tester's root. Without explicitly including them, Vite serves them via /@fs/
        // and skips the CommonJS->ESM transform, which causes missing default exports
        // (e.g., prop-types) when BookGridSetup loads.
        include: [
            "@mui/material",
            "@mui/material/styles",
            "@mui/material/styles/styled",
            "@mui/system",
            "@mui/styled-engine",
            "prop-types",
        ],
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

/**
 * Validates that a path parameter is safe from path traversal attacks.
 * Returns the sanitized path if valid, null otherwise.
 * @param pathParam - The path parameter to validate
 * @returns The sanitized path without leading slashes, or null if invalid
 */
const validateSafePath = (pathParam: string | null): string | null => {
    if (!pathParam) {
        return null;
    }

    // Remove leading slashes
    const sanitized = pathParam.replace(/^[\\/]+/, "");

    // Reject empty paths after sanitization
    if (!sanitized) {
        return null;
    }

    // Normalize the path to resolve any .. or . segments
    const normalized = normalize(sanitized);

    // Check for path traversal attempts
    // After normalization, any .. that escapes the current directory will start with ..
    if (
        normalized.startsWith("..") ||
        normalized.includes("..\\") ||
        normalized.includes("../")
    ) {
        return null;
    }

    // Reject absolute paths (Windows: C:\, \\, Unix: /)
    if (/^([a-zA-Z]:)?[\\/]/.test(normalized)) {
        return null;
    }

    return normalized;
};

/**
 * Validates that a book-id parameter contains only safe characters.
 * @param bookId - The book-id parameter to validate
 * @returns The book-id if valid, null otherwise
 */
const validateBookId = (bookId: string | null): string | null => {
    if (!bookId) {
        return null;
    }

    // Book IDs should only contain alphanumeric characters, hyphens, and underscores
    if (!/^[a-zA-Z0-9_-]+$/.test(bookId)) {
        return null;
    }

    return bookId;
};

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

        // Validate book-id parameter (required for all requests)
        const bookIdParam = url.searchParams.get("book-id");
        const validatedBookId = validateBookId(bookIdParam);
        if (!validatedBookId) {
            res.statusCode = 400;
            res.end("Missing or invalid book-id parameter");
            return;
        }

        // Validate file parameter
        const fileParam = url.searchParams.get("file");
        const validatedPath = validateSafePath(fileParam);
        if (!validatedPath) {
            res.statusCode = 400;
            res.end("Missing or invalid file parameter");
            return;
        }

        if (validatedPath === "appearance.css") {
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
    } catch (error) {
        // fall through
        console.error(
            "Component tester mock: failed to serve book file",
            error,
        );
    }
    res.statusCode = 404;
    res.end("Not Found");
};
