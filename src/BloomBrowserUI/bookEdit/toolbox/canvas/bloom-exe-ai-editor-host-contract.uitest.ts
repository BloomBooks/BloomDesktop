import { test, expect } from "playwright/test";
import { connectToBloomExe } from "../../../react_components/component-tester/bloomExeCdp";

// End-to-end tests for the AI Image Editor HOST CONTRACT (see AiImageEditorApi.cs). These
// attach to a real, running Bloom.exe over CDP and exercise the /file persistence endpoint and
// its session/allow-list gating — the parts of the integration that unit tests can't reach
// because they need the live server, a real session, and the on-disk .ai-image-editor folder.
//
// SCOPE: this is deliberately Bloom's side only. We do NOT drive the editor app or any AI
// generation — that lives in the separate bloom-ai-image-tools repo. A "generated image" here
// is just a file the editor would have written; we write it ourselves and check Bloom serves
// it back. So no OpenRouter/AI mock is involved.
//
// SAFETY: every test operates only inside the book's disposable .ai-image-editor/history/
// working folder, using clearly test-named files that it deletes again. It never commits a
// replacement, so the selected book's actual content is not modified.
//
// PRECONDITION: Bloom running in the Edit tab with a book selected (launch needs a current
// book). Run from src/BloomBrowserUI with Bloom up:
//   pnpm exec playwright test --config react_components/component-tester/playwright.bloom-exe.config.ts \
//       bookEdit/toolbox/canvas/bloom-exe-ai-editor-host-contract.uitest.ts

const kFileEndpoint = "/bloom/api/aiImageEditor/file";
const kLaunchEndpoint = "/bloom/api/aiImageEditor/launch";

// A 1x1 transparent PNG, base64. Small but a genuine image, so the GET path (ReplyWithImage)
// has real bytes to serve.
const kOnePixelPngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

test.describe("Bloom exe CDP: AI Image Editor host contract", () => {
    test("a history file round-trips through POST -> GET -> DELETE", async () => {
        const connection = await connectToBloomExe();
        try {
            const result = await connection.page.evaluate(
                async (args: {
                    fileEndpoint: string;
                    launchEndpoint: string;
                }) => {
                    const launch = await fetch(args.launchEndpoint, {
                        method: "POST",
                    });
                    if (!launch.ok) {
                        return {
                            launched: false,
                            detail: await launch.text(),
                        } as const;
                    }
                    const session = (
                        (await launch.json()) as { sessionToken: string }
                    ).sessionToken;

                    const name = "history/e2e-host-contract-roundtrip.json";
                    const url = `${args.fileEndpoint}?session=${encodeURIComponent(
                        session,
                    )}&name=${encodeURIComponent(name)}`;
                    const content = JSON.stringify({
                        e2e: true,
                        marker: "round-trip",
                    });

                    const post = await fetch(url, {
                        method: "POST",
                        body: content,
                    });
                    const get1 = await fetch(url);
                    const got = get1.ok ? await get1.text() : null;
                    const del = await fetch(url, { method: "DELETE" });
                    const get2 = await fetch(url);

                    return {
                        launched: true,
                        sessionPresent: !!session,
                        postOk: post.ok,
                        getStatus: get1.status,
                        got,
                        expected: content,
                        delOk: del.ok,
                        getAfterDeleteStatus: get2.status,
                    } as const;
                },
                {
                    fileEndpoint: kFileEndpoint,
                    launchEndpoint: kLaunchEndpoint,
                },
            );

            expect(
                result.launched,
                `launch failed (is a book selected in the Edit tab?): ${
                    "detail" in result ? result.detail : ""
                }`,
            ).toBe(true);
            if (!result.launched) return; // narrow the union; the assertion above already failed

            // Sanity: we actually got a session, so the gated calls below are meaningful.
            expect(
                result.sessionPresent,
                "launch returned no sessionToken",
            ).toBe(true);
            expect(
                result.postOk,
                "POST of a history sidecar should succeed",
            ).toBe(true);
            expect(result.getStatus, "the saved file should be readable").toBe(
                200,
            );
            expect(
                result.got,
                "GET should return exactly what POST wrote",
            ).toBe(result.expected);
            expect(result.delOk, "DELETE should succeed").toBe(true);
            expect(
                result.getAfterDeleteStatus,
                "the file should be gone after DELETE",
            ).toBe(404);
        } finally {
            await connection.browser.close();
        }
    });

    test("an image history file is accepted and served back as an image", async () => {
        const connection = await connectToBloomExe();
        try {
            const result = await connection.page.evaluate(
                async (args: {
                    fileEndpoint: string;
                    launchEndpoint: string;
                    pngBase64: string;
                }) => {
                    const launch = await fetch(args.launchEndpoint, {
                        method: "POST",
                    });
                    if (!launch.ok) {
                        return { launched: false } as const;
                    }
                    const session = (
                        (await launch.json()) as { sessionToken: string }
                    ).sessionToken;

                    const name = "history/e2e-host-contract-image.png";
                    const url = `${args.fileEndpoint}?session=${encodeURIComponent(
                        session,
                    )}&name=${encodeURIComponent(name)}`;
                    const bytes = Uint8Array.from(atob(args.pngBase64), (c) =>
                        c.charCodeAt(0),
                    );

                    const post = await fetch(url, {
                        method: "POST",
                        body: bytes,
                    });
                    const get1 = await fetch(url);
                    const contentType = get1.headers.get("content-type");
                    const del = await fetch(url, { method: "DELETE" });

                    return {
                        launched: true,
                        postOk: post.ok,
                        getStatus: get1.status,
                        contentType,
                        delOk: del.ok,
                    } as const;
                },
                {
                    fileEndpoint: kFileEndpoint,
                    launchEndpoint: kLaunchEndpoint,
                    pngBase64: kOnePixelPngBase64,
                },
            );

            expect(result.launched, "launch failed (is a book selected?)").toBe(
                true,
            );
            if (!result.launched) return;

            expect(
                result.postOk,
                "POST of a .png history file should succeed",
            ).toBe(true);
            expect(result.getStatus, "the image should be readable").toBe(200);
            expect(
                result.contentType,
                "an image file should come back with an image content-type",
            ).toMatch(/^image\//);
            expect(result.delOk).toBe(true);
        } finally {
            await connection.browser.close();
        }
    });

    test("the /file endpoint rejects requests with a missing or wrong session", async () => {
        const connection = await connectToBloomExe();
        try {
            const result = await connection.page.evaluate(
                async (args: {
                    fileEndpoint: string;
                    launchEndpoint: string;
                }) => {
                    const name = "history/e2e-host-contract-roundtrip.json";

                    // No session param at all.
                    const noSession = await fetch(
                        `${args.fileEndpoint}?name=${encodeURIComponent(name)}`,
                    );

                    // Launch so a valid session EXISTS, then present a wrong one — proving the
                    // 401 is a token mismatch, not merely "no session has ever been minted".
                    await fetch(args.launchEndpoint, { method: "POST" });
                    const wrongSession = await fetch(
                        `${args.fileEndpoint}?session=not-the-real-token&name=${encodeURIComponent(
                            name,
                        )}`,
                    );

                    return {
                        noSessionStatus: noSession.status,
                        wrongSessionStatus: wrongSession.status,
                    } as const;
                },
                {
                    fileEndpoint: kFileEndpoint,
                    launchEndpoint: kLaunchEndpoint,
                },
            );

            expect(
                result.noSessionStatus,
                "a request with no session must be unauthorized",
            ).toBe(401);
            expect(
                result.wrongSessionStatus,
                "a request with the wrong session token must be unauthorized",
            ).toBe(401);
        } finally {
            await connection.browser.close();
        }
    });

    test("the /file endpoint rejects file names outside the allow-list", async () => {
        const connection = await connectToBloomExe();
        try {
            const result = await connection.page.evaluate(
                async (args: {
                    fileEndpoint: string;
                    launchEndpoint: string;
                }) => {
                    const launch = await fetch(args.launchEndpoint, {
                        method: "POST",
                    });
                    if (!launch.ok) {
                        return { launched: false } as const;
                    }
                    const session = (
                        (await launch.json()) as { sessionToken: string }
                    ).sessionToken;

                    // Each of these must be refused: a top-level non-allowed file, a path
                    // traversal out of history/, an almost-but-not-quite state file, and a
                    // history image whose stem sneaks in a slash.
                    const badNames = [
                        "secret.txt",
                        "history/../evil.png",
                        "state.json.exe",
                        "history/pic/../../escape.png",
                    ];
                    const statuses: Record<string, number> = {};
                    for (const name of badNames) {
                        const url = `${args.fileEndpoint}?session=${encodeURIComponent(
                            session,
                        )}&name=${encodeURIComponent(name)}`;
                        const response = await fetch(url, {
                            method: "POST",
                            body: "should not be written",
                        });
                        statuses[name] = response.status;
                    }
                    return { launched: true, statuses } as const;
                },
                {
                    fileEndpoint: kFileEndpoint,
                    launchEndpoint: kLaunchEndpoint,
                },
            );

            expect(result.launched, "launch failed (is a book selected?)").toBe(
                true,
            );
            if (!result.launched) return;

            for (const [name, status] of Object.entries(result.statuses)) {
                expect(
                    status,
                    `'${name}' should be rejected as a bad file name`,
                ).toBe(400);
            }
        } finally {
            await connection.browser.close();
        }
    });
});
