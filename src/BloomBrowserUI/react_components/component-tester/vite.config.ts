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

import { spawn } from "node:child_process";
import { resolve } from "path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

const useMocks = process.env.BLOOM_COMPONENT_TESTER_USE_BACKEND !== "1";

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
                        // Mock books for link choosing - provides 4 books with 5 pages each
                        if (
                            req.url.startsWith(
                                "/bloom/api/collections/books?realTitle=true",
                            ) ||
                            req.url.startsWith(
                                "/bloom/api//collections/books?realTitle=true",
                            ) ||
                            req.url.startsWith(
                                "/bloom/api/collections/books?realTitle=false",
                            ) ||
                            req.url.startsWith(
                                "/bloom/api//collections/books?realTitle=false",
                            )
                        ) {
                            respondWithJson(res, [
                                {
                                    id: "book1",
                                    title: "First Book",
                                    folderName: "First Book",
                                    thumbnail:
                                        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%234CAF50'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 1%3C/text%3E%3C/svg%3E",
                                    pageLength: 5,
                                },
                                {
                                    id: "book2",
                                    title: "Second Book",
                                    folderName: "Second Book",
                                    thumbnail:
                                        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%232196F3'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 2%3C/text%3E%3C/svg%3E",
                                    pageLength: 5,
                                },
                                {
                                    id: "book3",
                                    title: "Third Book",
                                    folderName: "Third Book",
                                    thumbnail:
                                        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%23FF9800'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 3%3C/text%3E%3C/svg%3E",
                                    pageLength: 5,
                                },
                                {
                                    id: "book4",
                                    title: "Fourth Book",
                                    folderName: "Fourth Book",
                                    thumbnail:
                                        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='120'%3E%3Crect width='100' height='120' fill='%23E91E63'/%3E%3Ctext x='50' y='60' text-anchor='middle' fill='white' font-size='14'%3EBook 4%3C/text%3E%3C/svg%3E",
                                    pageLength: 5,
                                },
                            ]);
                            return;
                        }
                        if (
                            req.url.startsWith("/bloom/api/pageList/pages") ||
                            req.url.startsWith("/bloom/api//pageList/pages")
                        ) {
                            respondWithJson(res, {
                                pages: [
                                    { key: "cover", caption: "Cover" },
                                    { key: "1", caption: "Page 1" },
                                    { key: "2", caption: "Page 2" },
                                ],
                                selectedPageId: "cover",
                            });
                            return;
                        }
                        if (
                            req.url.startsWith(
                                "/bloom/api/pageList/pageContent",
                            ) ||
                            req.url.startsWith(
                                "/bloom/api//pageList/pageContent",
                            )
                        ) {
                            let pageId = "cover";
                            try {
                                const parsed = new URL(
                                    req.url,
                                    "http://localhost",
                                );
                                pageId =
                                    parsed.searchParams.get("id") ?? "cover";
                            } catch {}

                            const palette: Record<
                                string,
                                { color: string; caption: string }
                            > = {
                                cover: { color: "4CAF50", caption: "Cover" },
                                "1": { color: "2196F3", caption: "Page 1" },
                                "2": { color: "FF9800", caption: "Page 2" },
                            };
                            const selected = palette[pageId] ?? palette.cover;
                            const html = `<div class='bloom-page' inert><div style='width:160px;height:100px;background:#${selected.color};color:#fff;display:flex;align-items:center;justify-content:center;'>${selected.caption}</div></div>`;
                            respondWithJson(res, { data: { content: html } });
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
                            } catch {}
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
        port: 5173,
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
