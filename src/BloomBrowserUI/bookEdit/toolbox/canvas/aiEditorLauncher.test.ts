import { beforeEach, describe, expect, test, vi } from "vitest";

// Tests for the AI Image Editor launcher's two dealings with SAVING the live page: once
// before the editor opens, and once after a commit.
//
// BEFORE: the menu command doesn't open the editor at all. Everything C# tells the editor
// about the book is read from the saved book DOM, so an image the user just added — which
// lives only in the live page — wouldn't be there (BL-16682). So the command asks C# to
// save the page first, and C# calls openAiImageEditor() back in the reloaded page.
//
// AFTER: the launcher swaps current-page images in the LIVE DOM and then has to save,
// because nothing else persists that (see aiEditorLauncher.ts). That save goes through
// saveChangesAndRethinkPageEvent, which RELOADS the page frame — and everything that
// operates the overlay (its message listener, its ✕ handler) is code belonging to that
// frame, even though the overlay div itself lives in the top window. So saving while the
// overlay is still up leaves a full-screen overlay that nothing can close, and the user
// has to restart Bloom to escape. That matters specifically on a PARTIAL failure, where
// the launcher deliberately keeps the overlay up so the user can read the error.
//
// Hence: that save is deferred until the overlay comes down. These tests pin both halves —
// that it does not fire while the overlay is up, and that it is not simply lost.

const post = vi.fn();
const postJson = vi.fn();
const postThatMightNavigate = vi.fn();
const changeImageByElement = vi.fn();

vi.mock("../../../utils/bloomApi", () => ({
    post: (...args: unknown[]) => post(...args),
    postJson: (...args: unknown[]) => postJson(...args),
    postThatMightNavigate: (...args: unknown[]) =>
        postThatMightNavigate(...args),
}));

vi.mock("../../js/bloomEditing", () => ({
    changeImageByElement: (...args: unknown[]) => changeImageByElement(...args),
}));

vi.mock("../../js/bloomImages", () => ({
    getImageUrlFromImageContainer: (container: HTMLElement) =>
        container.getAttribute("data-url") ?? "",
    // The matcher compares filenames off the live elements; in the real thing this reads
    // an <img src> or a background-image url, which for our fixture is just the src.
    GetRawImageUrl: (element: HTMLElement) => element.getAttribute("src") ?? "",
}));

import { launchAiImageEditor, openAiImageEditor } from "./aiEditorLauncher";

const kSaveEvent = "common/saveChangesAndRethinkPageEvent";
const kEditorUrl = "http://localhost:8089/bloom/aiImageEditor/index.html";
const kPageId = "page1";
const kImageFile = "old.png";

// Builds a page holding one image and returns the img the user is to right-click.
const makePageWithOneImage = () => {
    document.body.innerHTML = `
        <div class="bloom-page" id="${kPageId}">
            <div class="bloom-canvas-element">
                <img src="${kImageFile}" />
            </div>
        </div>`;
    return document.querySelector("img") as HTMLImageElement;
};

// Runs the menu command and then stands in for C#, which saves the page (reloading it) and
// calls openAiImageEditor in the reloaded page. Returns the handles a test needs, with the
// overlay up and the editor about to be sent its `init`. Pass clickedFileName to stand for
// a click on an image the saved page does not contain.
const launchAgainstAPageWithOneImage = (clickedFileName = kImageFile) => {
    const img = makePageWithOneImage();

    launchAiImageEditor(img, undefined);

    // The command itself only asks for the save; the editor is not opened yet.
    expect(post).not.toHaveBeenCalled();
    expect(postJson).toHaveBeenCalledTimes(1);
    postJson.mockClear();

    // C#, once the saved page has reloaded.
    openAiImageEditor({ imageFileName: clickedFileName });

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
                    src: `http://localhost:8089/bloom/book/${kImageFile}`,
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

    return { closeButton, iframe, postFromEditor };
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

// Answers the editor's `ready` and returns the `init` the launcher posts back into its
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

const clearMocks = () => {
    post.mockClear();
    postJson.mockClear();
    postThatMightNavigate.mockClear();
    changeImageByElement.mockClear();
    delete (window as Window & { __bloomAiImageEditorCleanup?: () => void })
        .__bloomAiImageEditorCleanup;
    document.body.innerHTML = "";
};

describe("aiEditorLauncher: saving the live page before opening the editor", () => {
    beforeEach(clearMocks);

    test("the menu command asks C# to save the page instead of opening the editor", () => {
        const img = makePageWithOneImage();

        launchAiImageEditor(img, undefined);

        // Nothing is opened here: C# has to save the page (which reloads it) first, or the
        // editor would be told about the book as it was before the user's latest image
        // change (BL-16682).
        expect(post).not.toHaveBeenCalled();
        expect(postJson).toHaveBeenCalledTimes(1);
        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            // Only the file name travels: the save reloads this frame, so a live element
            // reference could not survive the round trip.
            imageFileName: kImageFile,
        });
    });

    test("the reopened editor gets the clicked image as its edit target", () => {
        const { iframe, postFromEditor } = launchAgainstAPageWithOneImage();

        const payload = getInitPayloadSentToEditor(iframe, postFromEditor);

        // The bug: this was undefined, so the "Image to Edit" slot opened empty.
        expect(payload.selectedBookImageId).toBe(`${kPageId}:0`);
    });

    test("an image the saved page doesn't have leaves the edit target unset", () => {
        // Sanity check on the matching itself: C#'s book image list names old.png, so a
        // click on some other file must not silently select old.png.
        const { iframe, postFromEditor } =
            launchAgainstAPageWithOneImage("somethingElse.png");

        const payload = getInitPayloadSentToEditor(iframe, postFromEditor);

        expect(payload.selectedBookImageId).toBeUndefined();
    });
});

describe("aiEditorLauncher: saving the live page after a commit", () => {
    beforeEach(clearMocks);

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
        expect(postThatMightNavigate).not.toHaveBeenCalled();

        closeButton.click();

        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(postThatMightNavigate).toHaveBeenCalledTimes(1);
        expect(postThatMightNavigate).toHaveBeenCalledWith(kSaveEvent);
    });

    test("a fully successful commit closes the overlay and saves", () => {
        const { postFromEditor } = launchAgainstAPageWithOneImage();

        commitAndReplyFromHost(postFromEditor, true);

        expect(changeImageByElement).toHaveBeenCalledTimes(1);
        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(postThatMightNavigate).toHaveBeenCalledTimes(1);
        expect(postThatMightNavigate).toHaveBeenCalledWith(kSaveEvent);
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
        expect(postThatMightNavigate).not.toHaveBeenCalled();

        // Closing must not conjure a save either — there was nothing to save, and an
        // unnecessary page reload would discard the user's unsaved text edits.
        closeButton.click();
        expect(postThatMightNavigate).not.toHaveBeenCalled();
    });
});
