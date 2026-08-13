import { beforeEach, describe, expect, test, vi } from "vitest";

// Tests for WHEN the AI Image Editor launcher saves the live page after a commit.
//
// The launcher swaps current-page images in the LIVE DOM and then has to save, because
// nothing else persists that (see aiEditorLauncher.ts). The save is deferred until the
// overlay comes down, so that one save covers however many commits the user makes, and so
// a PARTIAL failure — where the launcher deliberately keeps the overlay up for the user to
// read the error — is not saved halfway through. These tests pin both halves: that it does
// not fire while the overlay is up, and that it is not simply lost.
//
// It saves via savePageWithoutReloading(). It used to post saveChangesAndRethinkPageEvent,
// which RELOADED the page frame; since everything operating the overlay (its message
// listener, its ✕ handler) is code belonging to that frame even though the overlay div
// lives in the top window, saving while the overlay was up left a full-screen overlay that
// nothing could close. Saving without reloading removes that hazard (BL-13502).

const post = vi.fn();
const postJson = vi.fn();
const savePageWithoutReloading = vi.fn();
const changeImageByElement = vi.fn();

vi.mock("../../../utils/bloomApi", () => ({
    post: (...args: unknown[]) => post(...args),
    postJson: (...args: unknown[]) => postJson(...args),
    postThatMightNavigate: vi.fn(),
}));

vi.mock("../../js/bloomEditing", () => ({
    changeImageByElement: (...args: unknown[]) => changeImageByElement(...args),
    savePageWithoutReloading: (...args: unknown[]) =>
        savePageWithoutReloading(...args),
}));

vi.mock("../../js/bloomImages", () => ({
    getImageUrlFromImageContainer: (container: HTMLElement) =>
        container.getAttribute("data-url") ?? "",
    // The matcher compares filenames off the live elements; in the real thing this reads
    // an <img src> or a background-image url, which for our fixture is just the src.
    GetRawImageUrl: (element: HTMLElement) => element.getAttribute("src") ?? "",
}));

import { launchAiImageEditor } from "./aiEditorLauncher";

const kEditorUrl = "http://localhost:8089/bloom/aiImageEditor/index.html";
const kPageId = "page1";

// Builds a page holding one image, launches the editor against it, and returns the
// handles a test needs. Leaves the launcher at the point where the overlay is up and the
// editor has been sent its `init`.
const launchAgainstAPageWithOneImage = () => {
    document.body.innerHTML = `
        <div class="bloom-page" id="${kPageId}">
            <div class="bloom-canvas-element">
                <img src="old.png" />
            </div>
        </div>`;
    const img = document.querySelector("img") as HTMLImageElement;
    const canvasElement = document.querySelector(
        ".bloom-canvas-element",
    ) as HTMLElement;

    launchAiImageEditor(img, undefined, canvasElement);

    // The launcher does everything inside the launch reply's callback.
    expect(post).toHaveBeenCalledTimes(1);
    const launchCallback = post.mock.calls[0][1] as (r: {
        data: unknown;
    }) => void;
    launchCallback({
        data: {
            editorUrl: kEditorUrl,
            httpBase: "http://localhost:8089/bloom/api/aiImageEditor",
            sessionToken: "token123",
            book: { id: "book1", title: "Test Book" },
            bookImages: [
                {
                    id: `${kPageId}:0`,
                    src: "http://localhost:8089/bloom/book/old.png",
                },
            ],
            history: [],
        },
    });

    const overlay = document.getElementById("ai-editor-overlay");
    if (!overlay)
        throw new Error("setup: the launcher should have made an overlay");
    const iframe = overlay.querySelector("iframe") as HTMLIFrameElement;
    const closeButton = overlay.querySelector("button") as HTMLButtonElement;

    // Deliver a message as if it came from the editor's iframe. The launcher ignores
    // messages from anywhere else, so the source has to be the iframe's window.
    const postFromEditor = (data: unknown) => {
        window.dispatchEvent(
            new MessageEvent("message", {
                data,
                source: iframe.contentWindow,
            } as MessageEventInit),
        );
    };

    return { closeButton, postFromEditor };
};

// Sends a commit for the one current-page image and answers it with the host's reply.
// serverOk false is the partial-failure case: the host could not apply some OTHER slot
// (on a different page), while this page's swap succeeded.
const commitAndReplyFromHost = (
    postFromEditor: (data: unknown) => void,
    serverOk: boolean,
) => {
    postFromEditor({
        channel: "bloom-ai-image-tools",
        type: "commit",
        requestId: "req1",
        payload: {
            replacements: [{ incomingId: `${kPageId}:0`, resultId: "result1" }],
        },
    });

    expect(postJson).toHaveBeenCalledTimes(1);
    const onSuccess = postJson.mock.calls[0][2] as (r: {
        data: unknown;
    }) => void;
    onSuccess({
        data: {
            ok: serverOk,
            appliedCount: 1,
            results: [
                {
                    incomingId: `${kPageId}:0`,
                    ok: true,
                    isCurrentPage: true,
                    oldSrc: "old.png",
                    newSrc: "ai-image1.png",
                    copyright: "",
                    creator: "",
                    license: "",
                },
            ],
        },
    });
};

describe("aiEditorLauncher: saving the live page after a commit", () => {
    beforeEach(() => {
        post.mockClear();
        postJson.mockClear();
        savePageWithoutReloading.mockClear();
        changeImageByElement.mockClear();
        delete (window as Window & { __bloomAiImageEditorCleanup?: () => void })
            .__bloomAiImageEditorCleanup;
        document.body.innerHTML = "";
    });

    test("a partial failure keeps the overlay up and holds the save until it closes", () => {
        const { closeButton, postFromEditor } =
            launchAgainstAPageWithOneImage();

        commitAndReplyFromHost(postFromEditor, false);

        // Sanity: the current-page swap really did happen, so there IS something to save
        // and the assertions below aren't just watching a no-op.
        expect(changeImageByElement).toHaveBeenCalledTimes(1);

        // The overlay stays up so the user can read the error about the slot that failed.
        expect(document.getElementById("ai-editor-overlay")).not.toBeNull();
        // ...and therefore the save must NOT have fired: reloading the page frame now
        // would kill the ✕ below and trap the user behind the overlay.
        expect(savePageWithoutReloading).not.toHaveBeenCalled();

        closeButton.click();

        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(savePageWithoutReloading).toHaveBeenCalledTimes(1);
    });

    test("a fully successful commit closes the overlay and saves", () => {
        const { postFromEditor } = launchAgainstAPageWithOneImage();

        commitAndReplyFromHost(postFromEditor, true);

        expect(changeImageByElement).toHaveBeenCalledTimes(1);
        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(savePageWithoutReloading).toHaveBeenCalledTimes(1);
    });

    test("a commit that changed nothing on this page never saves", () => {
        const { closeButton, postFromEditor } =
            launchAgainstAPageWithOneImage();

        // The host applied everything itself (all the slots were off-page), so there is
        // no live-DOM change here to persist.
        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "commit",
            requestId: "req1",
            payload: {
                replacements: [{ incomingId: "page2:0", resultId: "result1" }],
            },
        });
        const onSuccess = postJson.mock.calls[0][2] as (r: {
            data: unknown;
        }) => void;
        onSuccess({
            data: {
                ok: true,
                appliedCount: 1,
                results: [
                    {
                        incomingId: "page2:0",
                        ok: true,
                        isCurrentPage: false,
                        oldSrc: "other.png",
                        newSrc: "ai-image1.png",
                    },
                ],
            },
        });

        expect(changeImageByElement).not.toHaveBeenCalled();
        expect(savePageWithoutReloading).not.toHaveBeenCalled();

        // Closing must not conjure a save either — there was nothing to save, and an
        // unnecessary page reload would discard the user's unsaved text edits.
        closeButton.click();
        expect(savePageWithoutReloading).not.toHaveBeenCalled();
    });
});
