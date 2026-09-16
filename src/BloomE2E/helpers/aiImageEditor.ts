// The "Edit with AI…" surface: Bloom's side of the AI image editor.
//
// The editor itself is a separate app (the bloom-ai-image-tools package) that Bloom copies into
// browser/aiImageEditor when it builds and then loads into an iframe overlay. Bloom's side of it is
// two things, and this module covers both: the launch that mints a session and says where the app
// is served, and the file endpoint the editor stores its working images and history through.
//
// Nothing here asks an AI for an image. A test writes the files the editor would have written and
// checks what Bloom does with them, so no OpenRouter key is involved. See
// src/BloomExe/web/controllers/AiImageEditorApi.cs.

import type { Page } from "@playwright/test";
import { apiPost } from "./api";

/** What Bloom hands the overlay when the editor opens. Only the fields tests read are listed. */
export interface IAiImageEditorSession {
    /** Where the editor app is served from, relative to Bloom's own origin. */
    editorUrl: string;
    /** The token every later file call has to present. */
    sessionToken: string;
}

/** How Bloom answered a file call, including a refusal. */
export interface IEditorFileResponse {
    status: number;
    body: string;
    contentType: string | null;
}

/** What a test asks Bloom to do with one of the editor's files. */
interface IEditorFileRequest {
    /** The session token, or undefined to send none at all. Sent exactly as given. */
    session?: string;
    /** The file's name within the book's editor folder, e.g. "history/whatever.png". */
    name: string;
    /** Text to store. */
    content?: string;
    /** Base64 of an image to store, instead of `content`, so real bytes reach Bloom. */
    pngBase64?: string;
}

/**
 * Open an AI image editor session for the book Bloom has selected, which is what choosing
 * "Edit with AI…" on an image does first.
 *
 * Throws when Bloom refuses. The refusal worth knowing is 503 "The AI Image Editor is not included
 * in this build of Bloom", which means the editor app was not staged into browser/aiImageEditor;
 * Bloom's own unit tests cannot see that, because they run against a stub editor.
 *
 * Select a book first (helpers/collection.ts `selectBook`). Without one Bloom answers "No book
 * selected". Each call ends the previous session and mints a new token, exactly as reopening the
 * editor does.
 */
export async function startAiImageEditorSession(
    page: Page,
): Promise<IAiImageEditorSession> {
    const reply = await apiPost(page, "aiImageEditor/launch");
    const session = JSON.parse(reply.body) as IAiImageEditorSession;
    if (!session.editorUrl || !session.sessionToken)
        throw new Error(
            "aiImageEditor/launch answered 200 but without the fields the overlay needs. " +
                `Got: ${reply.body.slice(0, 300)}`,
        );
    return session;
}

/**
 * Load the editor app the way the overlay does and wait for the app's own "ready" handshake, which
 * it posts only once its HTML and JS have loaded and its host component has mounted. So a ready
 * handshake is the evidence that the app genuinely booted rather than 404'd into an empty frame.
 *
 * Returns rather than throws, so the caller's assertion carries the reason it gave up. The iframe
 * is removed again either way. This is not the journey path: opening the overlay for real goes
 * through the image's context menu, and only that registers the overlay's own message handler
 * (see .github/skills/bloom-automation/ai-image-editor-driving.md).
 */
export async function bootAiImageEditorInIframe(
    page: Page,
    editorUrl: string,
    timeoutMs = 30000,
): Promise<{ booted: boolean; detail: string }> {
    return await page.evaluate(
        async (args: { editorUrl: string; timeoutMs: number }) => {
            const iframeUrl = new URL(args.editorUrl, window.location.href);
            iframeUrl.searchParams.set("mode", "bloom-iframe");

            return await new Promise<{ booted: boolean; detail: string }>(
                (resolve) => {
                    const iframe = document.createElement("iframe");
                    // Off-screen: whether it boots is the only question, and Bloom's UI is showing.
                    iframe.style.cssText =
                        "position:fixed;left:-99999px;top:0;width:1024px;height:768px;border:0;";

                    let settled = false;
                    const finish = (booted: boolean, detail: string) => {
                        if (settled) return;
                        settled = true;
                        window.removeEventListener("message", onMessage);
                        clearTimeout(timer);
                        iframe.remove();
                        resolve({ booted, detail });
                    };

                    const onMessage = (event: MessageEvent) => {
                        if (event.source !== iframe.contentWindow) return;
                        const data = event.data as {
                            channel?: string;
                            type?: string;
                        };
                        if (
                            data?.channel === "bloom-ai-image-tools" &&
                            data?.type === "ready"
                        )
                            finish(true, "the editor sent its ready handshake");
                    };

                    const timer = setTimeout(
                        () =>
                            finish(
                                false,
                                `no ready handshake from ${iframeUrl.pathname} within ${args.timeoutMs}ms; ` +
                                    "the editor app did not finish booting",
                            ),
                        args.timeoutMs,
                    );
                    iframe.addEventListener("error", () =>
                        finish(
                            false,
                            `the iframe failed to load ${iframeUrl.pathname}`,
                        ),
                    );

                    window.addEventListener("message", onMessage);
                    iframe.src = iframeUrl.toString();
                    document.body.appendChild(iframe);
                },
            );
        },
        { editorUrl, timeoutMs },
    );
}

/**
 * Store one of the editor's files in the book's editor folder, as the editor does when it saves a
 * result or its history.
 *
 * These four file helpers report Bloom's answer instead of throwing on a refusal, which is why they
 * do not use helpers/api.ts: refusing is half of what this endpoint is for, so the status is the
 * result a test reads. Like those helpers they go through the page, because Bloom's server accepts
 * only a "localhost" Host header (see the header comment of helpers/api.ts).
 */
export async function writeEditorFile(
    page: Page,
    request: IEditorFileRequest,
): Promise<IEditorFileResponse> {
    return callFileEndpoint(page, "POST", request);
}

/** Read one of the editor's files back, as the editor does when it reopens a book's history. */
export async function readEditorFile(
    page: Page,
    request: IEditorFileRequest,
): Promise<IEditorFileResponse> {
    return callFileEndpoint(page, "GET", request);
}

/** Discard one of the editor's files, as the editor does when the user drops a history entry. */
export async function deleteEditorFile(
    page: Page,
    request: IEditorFileRequest,
): Promise<IEditorFileResponse> {
    return callFileEndpoint(page, "DELETE", request);
}

async function callFileEndpoint(
    page: Page,
    method: "GET" | "POST" | "DELETE",
    request: IEditorFileRequest,
): Promise<IEditorFileResponse> {
    return await page.evaluate(
        async (call: {
            method: string;
            session?: string;
            name: string;
            content?: string;
            pngBase64?: string;
        }) => {
            const session =
                call.session === undefined
                    ? ""
                    : `session=${encodeURIComponent(call.session)}&`;
            const url = `/bloom/api/aiImageEditor/file?${session}name=${encodeURIComponent(
                call.name,
            )}`;
            // Decoding inside the page keeps the bytes binary: a Uint8Array cannot survive the
            // trip through page.evaluate's argument serialization.
            const body = call.pngBase64
                ? Uint8Array.from(atob(call.pngBase64), (c) => c.charCodeAt(0))
                : call.content;
            const response = await fetch(url, { method: call.method, body });
            return {
                status: response.status,
                body: await response.text(),
                contentType: response.headers.get("content-type"),
            };
        },
        { method, ...request },
    );
}
