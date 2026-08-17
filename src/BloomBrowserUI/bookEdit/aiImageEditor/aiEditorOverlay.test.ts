import { beforeEach, describe, expect, test, vi } from "vitest";

// Tests for the top-window half of the AI Image Editor integration: the overlay and its
// conversation with the editor's iframe.
//
// Two things this half is responsible for, both of which used to be tangled up with the
// live page and are pinned here:
//
//  - The edit target. C# hands over the page id and file name of the image the user
//    right-clicked (it survived a page save, which reloaded the page frame), and the overlay
//    matches that against the book image list to fill the "Image to Edit" slot (BL-16682).
//  - Saving after a commit. The current-page swaps only touched the LIVE DOM, so unless we
//    save, a second commit in the same session would read its oldSrc from a saved page still
//    showing the pre-edit image and match nothing. We save immediately, via the page frame's
//    savePageWithoutReloading, which leaves the page under the overlay alone (BL-13502).

const post = vi.fn();
const postJson = vi.fn();
const savePageWithoutReloading = vi.fn();
const applyAiImageEditorReplacements = vi.fn();
const getEditablePageBundleExports = vi.fn();

vi.mock("../../utils/bloomApi", () => ({
    post: (...args: unknown[]) => post(...args),
    postJson: (...args: unknown[]) => postJson(...args),
    postThatMightNavigate: vi.fn(),
}));

vi.mock("../js/workspaceFrames", () => ({
    getEditablePageBundleExports: () => getEditablePageBundleExports(),
}));

import { openAiImageEditor } from "./aiEditorOverlay";

const kEditorUrl = "http://localhost:8089/bloom/aiImageEditor/index.html";
const kPageId = "page1";
const kImageFile = "old.png";

// Opens the overlay as C# does, and answers the launch request as C# would. Returns the
// handles a test needs, with the overlay up and the editor about to be sent its `init`.
const openAgainstABookWithOneImage = (
    target = { pageId: kPageId, imageFileName: kImageFile },
    bookImages: Array<{ id: string; src: string; isPlaceholder?: boolean }> = [
        {
            id: `${kPageId}:0`,
            src: `http://localhost:8089/bloom/book/${kImageFile}`,
        },
    ],
) => {
    openAiImageEditor(target);

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
            bookImages,
            history: [],
        },
    });

    const overlay = document.getElementById("ai-editor-overlay");
    if (!overlay)
        throw new Error("setup: the overlay should have been created");
    const iframe = overlay.querySelector("iframe") as HTMLIFrameElement;
    const closeButton = overlay.querySelector("button") as HTMLButtonElement;

    // Deliver a message as if it came from the editor's iframe. The overlay ignores
    // messages from anywhere else, so the source has to be the iframe's window.
    const postFromEditor = (data: unknown) => {
        window.dispatchEvent(
            new MessageEvent("message", {
                data,
                source: iframe.contentWindow,
            } as MessageEventInit),
        );
    };

    return { closeButton, iframe, postFromEditor };
};

// Answers the editor's `ready` and returns the `init` the overlay posts back into its
// iframe — which is where the edit target ("Image to Edit") is named.
const getInitPayloadSentToEditor = (
    iframe: HTMLIFrameElement,
    postFromEditor: (data: unknown) => void,
) => {
    const postMessageToEditor = vi.spyOn(iframe.contentWindow!, "postMessage");
    postFromEditor({ channel: "bloom-ai-image-tools", type: "ready" });

    expect(postMessageToEditor).toHaveBeenCalledTimes(1);
    const message = postMessageToEditor.mock.calls[0][0] as {
        type: string;
        payload: { selectedBookImageId?: string };
    };
    postMessageToEditor.mockRestore();
    expect(message.type).toBe("init");
    return message.payload;
};

// Sends a commit for one current-page image and answers it with C#'s reply. serverOk false
// is the partial-failure case: C# could not apply some OTHER slot (on a different page),
// while this page's swap succeeded.
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
                    oldSrc: kImageFile,
                    newSrc: "ai-image1.png",
                },
            ],
        },
    });
};

beforeEach(() => {
    post.mockClear();
    postJson.mockClear();
    savePageWithoutReloading.mockClear();
    // It answers whether C# actually saved; the overlay chains onto that to complain if not.
    savePageWithoutReloading.mockResolvedValue(true);
    applyAiImageEditorReplacements.mockClear();
    applyAiImageEditorReplacements.mockReturnValue({
        applied: 1,
        expected: 1,
    });
    getEditablePageBundleExports.mockReturnValue({
        applyAiImageEditorReplacements,
        savePageWithoutReloading,
    });
    delete (window as Window & { __bloomAiImageEditorCleanup?: () => void })
        .__bloomAiImageEditorCleanup;
    document.body.innerHTML = "";
});

describe("aiEditorOverlay: the edit target", () => {
    test("the image C# names becomes the editor's edit target", () => {
        const { iframe, postFromEditor } = openAgainstABookWithOneImage();

        const payload = getInitPayloadSentToEditor(iframe, postFromEditor);

        // The bug: this was undefined, so the "Image to Edit" slot opened empty.
        expect(payload.selectedBookImageId).toBe(`${kPageId}:0`);
    });

    test("an image the saved book doesn't have leaves the target unset", () => {
        // Sanity check on the matching: the book image list names old.png, so a click on
        // some other file must not silently select old.png.
        const { iframe, postFromEditor } = openAgainstABookWithOneImage({
            pageId: kPageId,
            imageFileName: "somethingElse.png",
        });

        const payload = getInitPayloadSentToEditor(iframe, postFromEditor);

        expect(payload.selectedBookImageId).toBeUndefined();
    });

    test("a matching slot on a different page is not selected", () => {
        const { iframe, postFromEditor } = openAgainstABookWithOneImage(
            { pageId: "page2", imageFileName: kImageFile },
            [
                {
                    id: `${kPageId}:0`,
                    src: `http://localhost:8089/bloom/book/${kImageFile}`,
                },
            ],
        );

        const payload = getInitPayloadSentToEditor(iframe, postFromEditor);

        expect(payload.selectedBookImageId).toBeUndefined();
    });

    test("an empty placeholder slot is not preloaded as the target", () => {
        // There is nothing to edit, and the placeholder graphic isn't a real raster image.
        const { iframe, postFromEditor } = openAgainstABookWithOneImage(
            { pageId: kPageId, imageFileName: "placeHolder.png" },
            [
                {
                    id: `${kPageId}:0`,
                    src: "http://localhost:8089/bloom/book/placeHolder.png",
                    isPlaceholder: true,
                },
            ],
        );

        const payload = getInitPayloadSentToEditor(iframe, postFromEditor);

        expect(payload.selectedBookImageId).toBeUndefined();
    });
});

describe("aiEditorOverlay: saving the live page after a commit", () => {
    test("a successful commit closes the overlay and saves at once", () => {
        const { postFromEditor } = openAgainstABookWithOneImage();

        commitAndReplyFromHost(postFromEditor, true);

        // Sanity: the current-page swap really was requested of the page frame, so the
        // assertions below aren't just watching a no-op.
        expect(applyAiImageEditorReplacements).toHaveBeenCalledTimes(1);
        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(savePageWithoutReloading).toHaveBeenCalledTimes(1);
    });

    test("a save Bloom refuses is complained about, not swallowed", async () => {
        // Bloom can decline (the user may have started changing pages). The book on disk then
        // still has the old image, which is exactly what this save exists to prevent, so the
        // least we can do is say so rather than let it look like it worked.
        const error = vi.spyOn(console, "error").mockImplementation(() => {});
        savePageWithoutReloading.mockResolvedValue(false);
        const { postFromEditor } = openAgainstABookWithOneImage();

        commitAndReplyFromHost(postFromEditor, true);
        await vi.waitFor(() => expect(error).toHaveBeenCalled());

        expect(error.mock.calls[0][0]).toContain("declined to save");
        error.mockRestore();
    });

    test("a partial failure keeps the overlay up AND still saves what landed", () => {
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        commitAndReplyFromHost(postFromEditor, false);

        // The overlay stays up so the user can read the error about the slot that failed —
        // and, unlike when this code lived in the page frame, saving now does not endanger
        // it, so the swap that did land is persisted immediately rather than held hostage
        // until the user closes the overlay.
        expect(document.getElementById("ai-editor-overlay")).not.toBeNull();
        expect(savePageWithoutReloading).toHaveBeenCalledTimes(1);

        // The ✕ still works after that save, because these controls belong to the top
        // window, not to the page frame the save reloaded.
        closeButton.click();
        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(savePageWithoutReloading).toHaveBeenCalledTimes(1);
    });

    test("a commit that changed nothing on this page never saves", () => {
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 0,
            expected: 0,
        });
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        // C# applied everything itself (all the slots were off-page), so there is no
        // live-DOM change here to persist.
        commitAndReplyFromHost(postFromEditor, true);

        expect(savePageWithoutReloading).not.toHaveBeenCalled();
        closeButton.click();
        expect(savePageWithoutReloading).not.toHaveBeenCalled();
    });

    test("a failed swap's reason reaches the editor, not just the count", () => {
        // The page frame reports a throw as a return value, so the overlay has to append
        // its reason itself or the user only ever sees "Only 1 of 2 …".
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 1,
            expected: 2,
            error: "kaboom",
        });
        const { iframe, postFromEditor } = openAgainstABookWithOneImage();
        const postMessageToEditor = vi.spyOn(
            iframe.contentWindow!,
            "postMessage",
        );

        commitAndReplyFromHost(postFromEditor, true);

        const ack = postMessageToEditor.mock.calls[0][0] as {
            ok: boolean;
            error?: string;
        };
        expect(ack.ok).toBe(false);
        expect(ack.error).toContain("Only 1 of 2");
        expect(ack.error).toContain("kaboom");
        // What did land still gets saved.
        postMessageToEditor.mockRestore();
    });

    test("an all-off-page commit succeeds even if the page frame is unreachable", () => {
        // The page frame is briefly null while it reloads — which this feature's own
        // post-commit save causes. Asking for it when the commit has nothing to do on the
        // open page reported an error for images C# had in fact replaced and saved, and
        // invited a retry that would redo them and orphan the files.
        getEditablePageBundleExports.mockReturnValue(null);
        const { iframe, postFromEditor } = openAgainstABookWithOneImage();
        const postMessageToEditor = vi.spyOn(
            iframe.contentWindow!,
            "postMessage",
        );

        // C# applied everything itself; nothing is flagged isCurrentPage.
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

        const ack = postMessageToEditor.mock.calls[0][0] as {
            ok: boolean;
            error?: string;
        };
        expect(ack.ok).toBe(true);
        expect(ack.error).toBeUndefined();
        // Nothing landed on this page, so nothing to save.
        expect(savePageWithoutReloading).not.toHaveBeenCalled();
        expect(applyAiImageEditorReplacements).not.toHaveBeenCalled();
        postMessageToEditor.mockRestore();
    });

    test("acks a failure when the page frame is unreachable", () => {
        // Fail loudly rather than silently reporting success for swaps that never happened.
        getEditablePageBundleExports.mockReturnValue(null);
        const { iframe, postFromEditor } = openAgainstABookWithOneImage();
        const postMessageToEditor = vi.spyOn(
            iframe.contentWindow!,
            "postMessage",
        );

        commitAndReplyFromHost(postFromEditor, true);

        const ack = postMessageToEditor.mock.calls[0][0] as {
            type: string;
            ok: boolean;
            error?: string;
        };
        expect(ack.type).toBe("ack");
        expect(ack.ok).toBe(false);
        expect(ack.error).toContain("not available");
        expect(ack.error).toContain("other pages were made");
        // Nothing landed, so nothing to save.
        expect(savePageWithoutReloading).not.toHaveBeenCalled();
        postMessageToEditor.mockRestore();
    });
});
