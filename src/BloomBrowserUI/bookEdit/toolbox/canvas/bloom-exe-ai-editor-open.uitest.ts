import { test, expect } from "playwright/test";
import { connectToBloomExe } from "../../../react_components/component-tester/bloomExeCdp";

// End-to-end smoke test for the "Edit with AI..." integration (see
// canvasControlRegistry.ts `editWithAi` and AiImageEditorApi.cs). It attaches to a
// real, running Bloom.exe over CDP and proves the editor can actually OPEN — the exact
// thing that silently 503'd when the editor app wasn't staged into the build.
//
// PRECONDITION: Bloom is running in the Edit tab with a book selected (the launch
// endpoint needs a current book). Run it with the app up, e.g. from src/BloomBrowserUI:
//   pnpm exec playwright test --config react_components/component-tester/playwright.bloom-exe.config.ts \
//       bookEdit/toolbox/canvas/bloom-exe-ai-editor-open.uitest.ts
//
// It deliberately stops short of driving the right-click menu (that is the heavier
// "full UI" flow); instead it exercises the launch contract and confirms the editor
// iframe boots, which is what "we can open the AI editor" really means.
test.describe("Bloom exe CDP: AI Image Editor", () => {
    test("launches and the editor app boots in its iframe", async () => {
        const connection = await connectToBloomExe();

        try {
            // 1. The launch endpoint mints a session and returns everything the overlay
            //    needs, including where the editor app is served (editorUrl). This is the
            //    call that returned 503 "not included in this build" before staging worked.
            const launch = await connection.page.evaluate(async () => {
                const response = await fetch(
                    "/bloom/api/aiImageEditor/launch",
                    { method: "POST" },
                );
                return {
                    status: response.status,
                    body: response.ok
                        ? await response.json()
                        : await response.text(),
                };
            });

            expect(
                launch.status,
                `aiImageEditor/launch failed (is a book selected in the Edit tab?): ${JSON.stringify(
                    launch.body,
                )}`,
            ).toBe(200);

            const launchData = launch.body as {
                editorUrl: string;
                sessionToken: string;
                bookImages?: unknown[];
            };
            expect(
                launchData.editorUrl,
                "launch reply had no editorUrl",
            ).toBeTruthy();
            expect(
                launchData.sessionToken,
                "launch reply had no sessionToken",
            ).toBeTruthy();

            // 2. Prove the editor really opens: load it in an iframe exactly as the overlay
            //    does (mode=bloom-iframe) and wait for the editor's own "ready" handshake on
            //    the shared "bloom-ai-image-tools" channel. That message only fires once the
            //    app's HTML + JS have loaded and BloomHostedImageEditor has mounted, so it is
            //    the strongest signal that the editor genuinely booted rather than 404'd.
            const bootResult = await connection.page.evaluate(
                async (editorUrl: string) => {
                    const iframeUrl = new URL(editorUrl, window.location.href);
                    iframeUrl.searchParams.set("mode", "bloom-iframe");

                    return await new Promise<{
                        ready: boolean;
                        reason: string;
                    }>((resolve) => {
                        const iframe = document.createElement("iframe");
                        iframe.id = "ai-editor-smoke-iframe";
                        // Keep it out of sight; we only care that it boots.
                        iframe.style.cssText =
                            "position:fixed;left:-99999px;top:0;width:1024px;height:768px;border:0;";

                        let settled = false;
                        const cleanup = () => {
                            window.removeEventListener("message", onMessage);
                            clearTimeout(timer);
                            iframe.remove();
                        };
                        const finish = (ready: boolean, reason: string) => {
                            if (settled) return;
                            settled = true;
                            cleanup();
                            resolve({ ready, reason });
                        };

                        const onMessage = (event: MessageEvent) => {
                            if (event.source !== iframe.contentWindow) {
                                return;
                            }
                            const data = event.data as {
                                channel?: string;
                                type?: string;
                            };
                            if (
                                data?.channel === "bloom-ai-image-tools" &&
                                data?.type === "ready"
                            ) {
                                finish(true, "received ready handshake");
                            }
                        };

                        const timer = setTimeout(
                            () =>
                                finish(
                                    false,
                                    "timed out waiting for the editor's ready handshake",
                                ),
                            20000,
                        );

                        iframe.addEventListener("error", () =>
                            finish(false, "iframe failed to load"),
                        );

                        window.addEventListener("message", onMessage);
                        iframe.src = iframeUrl.toString();
                        document.body.appendChild(iframe);
                    });
                },
                launchData.editorUrl,
            );

            expect(bootResult.ready, bootResult.reason).toBe(true);
        } finally {
            await connection.browser.close();
        }
    });
});
