// The "Edit with AI…" integration: Bloom's side of the separate bloom-ai-image-tools app, which
// Bloom stages into browser/aiImageEditor at build time and loads into an iframe overlay.
//
// SCOPE: Bloom's side only. Nothing here drives the editor app or asks any AI for an image — that
// belongs to the bloom-ai-image-tools repository. A "generated image" here is only a file the
// editor would have written; the test writes it and checks Bloom serves it back. So no OpenRouter
// key and no AI mock is involved.
//
// These are the two things Bloom's unit tests cannot reach. The first is that the editor app is
// really in the build: the Vitest suite exercises Bloom's half of the postMessage protocol against
// a stub, so it passes just as happily when browser/aiImageEditor is missing entirely, which is how
// this silently broke before. The second is the file endpoint, which needs the live server, a real
// session token, and a book folder on disk.
//
// The UI path — right-click an image, choose "Edit with AI…" — is not covered here and has no
// journey test yet; Notion Test Case ID 805 still runs it by hand.

import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { selectBook } from "../helpers/collection";
import {
    bootAiImageEditorInIframe,
    deleteEditorFile,
    readEditorFile,
    startAiImageEditorSession,
    writeEditorFile,
} from "../helpers/aiImageEditor";

test.use({ collectionName: "basic" });

// A 1x1 transparent PNG. Small, but a genuine image, so the read path has real bytes to serve.
const kOnePixelPngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

/**
 * Get Bloom to the state the editor opens from. Selecting a book is the whole precondition: the
 * launch refuses with "No book selected" and nothing else. We stay on the collection tab, because
 * launching does not care which tab is showing and a tab switch reloads a whole webview.
 */
async function openEditorOnABook(page: Page, collectionDir: string) {
    await selectBook(page, Path.join(collectionDir, "A5 Portrait"));
    return await startAiImageEditorSession(page);
}

test("the AI image editor app is in the build and boots in its iframe [Test Case ID 816]", async ({
    page,
    bloomApp,
}) => {
    const session = await openEditorOnABook(page, bloomApp.collectionDir);

    const boot = await bootAiImageEditorInIframe(page, session.editorUrl);

    expect(boot.booted, boot.detail).toBe(true);
});

test("a history file written by the editor round-trips through write, read and delete [Test Case ID 817]", async ({
    page,
    bloomApp,
}) => {
    const session = await openEditorOnABook(page, bloomApp.collectionDir);
    const file = {
        session: session.sessionToken,
        name: "history/e2e-host-contract-roundtrip.json",
    };
    const content = JSON.stringify({ e2e: true, marker: "round-trip" });

    const written = await writeEditorFile(page, { ...file, content });
    expect(written.status, "writing a history sidecar should succeed").toBe(
        200,
    );

    const read = await readEditorFile(page, file);
    expect(read.status, "the file just written should be readable").toBe(200);
    expect(read.body, "reading should return exactly what was written").toBe(
        content,
    );

    const discarded = await deleteEditorFile(page, file);
    expect(discarded.status, "deleting the file should succeed").toBe(200);

    const readAgain = await readEditorFile(page, file);
    expect(readAgain.status, "the file should be gone after deleting").toBe(
        404,
    );
});

test("an image the editor saved is served back as an image [Test Case ID 817]", async ({
    page,
    bloomApp,
}) => {
    const session = await openEditorOnABook(page, bloomApp.collectionDir);
    const file = {
        session: session.sessionToken,
        name: "history/e2e-host-contract-image.png",
    };

    const written = await writeEditorFile(page, {
        ...file,
        pngBase64: kOnePixelPngBase64,
    });
    expect(written.status, "writing a .png history file should succeed").toBe(
        200,
    );

    const read = await readEditorFile(page, file);
    expect(read.status, "the image should be readable").toBe(200);
    // The editor puts what comes back straight into an <img>, so the type has to say image.
    expect(
        read.contentType,
        "an image file should come back with an image content-type",
    ).toMatch(/^image\//);

    await deleteEditorFile(page, file);
});

test("the file endpoint refuses a request with a missing or wrong session [Test Case ID 817]", async ({
    page,
    bloomApp,
}) => {
    const name = "history/e2e-host-contract-roundtrip.json";

    const noSession = await readEditorFile(page, { name });
    expect(
        noSession.status,
        "a request carrying no session must be refused",
    ).toBe(401);

    // Mint a real session before presenting a wrong one, so the refusal below proves the token is
    // actually compared rather than that no session had ever been created.
    await openEditorOnABook(page, bloomApp.collectionDir);

    const wrongSession = await readEditorFile(page, {
        session: "not-the-real-token",
        name,
    });
    expect(
        wrongSession.status,
        "a request carrying the wrong session token must be refused",
    ).toBe(401);
});

test("the file endpoint refuses file names outside its allow-list [Test Case ID 817]", async ({
    page,
    bloomApp,
}) => {
    const session = await openEditorOnABook(page, bloomApp.collectionDir);

    // A file at the top level, an escape out of the history folder, a state file with an
    // executable extension tacked on, and a history image whose stem hides a traversal.
    const refusedNames = [
        "secret.txt",
        "history/../evil.png",
        "state.json.exe",
        "history/pic/../../escape.png",
    ];

    for (const name of refusedNames) {
        const attempt = await writeEditorFile(page, {
            session: session.sessionToken,
            name,
            content: "should not be written",
        });
        expect(
            attempt.status,
            `'${name}' should be refused as a bad file name`,
        ).toBe(400);
    }
});
