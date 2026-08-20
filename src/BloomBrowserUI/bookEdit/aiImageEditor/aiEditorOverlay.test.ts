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
//    showing the pre-edit image and match nothing. Because this overlay lives in the top
//    window, we can save immediately: the page reload underneath leaves its controls alone.

const post = vi.fn();
const postJson = vi.fn();
const postThatMightNavigate = vi.fn();
const trackEvent = vi.fn();
const trackChangePicture = vi.fn();
const applyAiImageEditorReplacements = vi.fn();
const getEditablePageBundleExports = vi.fn();

vi.mock("../../utils/bloomApi", () => ({
    post: (...args: unknown[]) => post(...args),
    postJson: (...args: unknown[]) => postJson(...args),
    postThatMightNavigate: (...args: unknown[]) =>
        postThatMightNavigate(...args),
    trackEvent: (...args: unknown[]) => trackEvent(...args),
    trackChangePicture: (...args: unknown[]) => trackChangePicture(...args),
}));

vi.mock("../js/workspaceFrames", () => ({
    getEditablePageBundleExports: () => getEditablePageBundleExports(),
}));

import { openAiImageEditor } from "./aiEditorOverlay";

const kSaveEvent = "common/saveChangesAndRethinkPageEvent";
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
    postThatMightNavigate.mockClear();
    trackEvent.mockClear();
    trackChangePicture.mockClear();
    applyAiImageEditorReplacements.mockClear();
    applyAiImageEditorReplacements.mockReturnValue({
        applied: 1,
        expected: 1,
    });
    getEditablePageBundleExports.mockReturnValue({
        applyAiImageEditorReplacements,
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
        expect(postThatMightNavigate).toHaveBeenCalledTimes(1);
        expect(postThatMightNavigate).toHaveBeenCalledWith(kSaveEvent);
    });

    test("a partial failure keeps the overlay up AND still saves what landed", () => {
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        commitAndReplyFromHost(postFromEditor, false);

        // The overlay stays up so the user can read the error about the slot that failed —
        // and, unlike when this code lived in the page frame, saving now does not endanger
        // it, so the swap that did land is persisted immediately rather than held hostage
        // until the user closes the overlay.
        expect(document.getElementById("ai-editor-overlay")).not.toBeNull();
        expect(postThatMightNavigate).toHaveBeenCalledTimes(1);
        expect(postThatMightNavigate).toHaveBeenCalledWith(kSaveEvent);

        // The ✕ still works after that save, because these controls belong to the top
        // window, not to the page frame the save reloaded.
        closeButton.click();
        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(postThatMightNavigate).toHaveBeenCalledTimes(1);
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

        expect(postThatMightNavigate).not.toHaveBeenCalled();
        closeButton.click();
        expect(postThatMightNavigate).not.toHaveBeenCalled();
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
        expect(postThatMightNavigate).toHaveBeenCalledWith(kSaveEvent);
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
        expect(postThatMightNavigate).not.toHaveBeenCalled();
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
        expect(postThatMightNavigate).not.toHaveBeenCalled();
        postMessageToEditor.mockRestore();
    });
});

describe("aiEditorOverlay: analytics", () => {
    // The value of the cancel event is that it says how much AI work was thrown away, so it
    // must fire when a session ends without committing -- and must NOT fire when the session
    // ended because the work was accepted.
    const cancelEvents = () =>
        trackEvent.mock.calls.filter((call) => call[0] === "AI Editor Cancel");

    test("closing without committing reports a cancel, with what was generated", () => {
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();
        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "analytics",
            payload: {
                event: "AI Editor Generate",
                properties: { model: "some-model", result: "success" },
            },
        });

        // Sanity: the editor's own event was passed straight through.
        expect(trackEvent).toHaveBeenCalledWith("AI Editor Generate", {
            model: "some-model",
            result: "success",
        });
        expect(cancelEvents()).toHaveLength(0);

        closeButton.click();

        expect(cancelEvents()).toHaveLength(1);
        expect(cancelEvents()[0][1]).toMatchObject({ generatedThisSession: 1 });
    });

    test("an event name we do not know is ignored, and does not break the session", () => {
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        // "toString" is the interesting case rather than a random word: with an object literal
        // instead of a Set, `"toString" in list` answers true, so the name would be treated as one
        // we know and would go on to create a junk event type in our data.
        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "analytics",
            payload: {
                event: "toString",
                properties: { prompt: "leaked book text" },
            },
        });

        expect(trackEvent).not.toHaveBeenCalled();

        // The session must still be alive: closing still reports the cancel.
        closeButton.click();
        expect(cancelEvents()).toHaveLength(1);
    });

    test("the properties of a known event are passed on as the ai-editor sent them", () => {
        // Deliberately not filtered: we control both ends of this channel. If a property ever must
        // not be forwarded, it is stopped in the ai-editor or removed by name here -- not by an
        // allow-list that only guards us against ourselves.
        const { postFromEditor } = openAgainstABookWithOneImage();

        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "analytics",
            payload: {
                event: "AI Editor Generate",
                properties: {
                    model: "some-model",
                    costUSD: 0.0733,
                    spentCredits: true,
                },
            },
        });

        expect(trackEvent).toHaveBeenCalledWith("AI Editor Generate", {
            model: "some-model",
            costUSD: 0.0733,
            spentCredits: true,
        });
    });

    test("closing while a commit is in flight does not report a cancel as well", () => {
        // The overlay goes away the moment the user clicks the close box, but the pictures may be
        // saved a moment later. Reporting a cancel here would count one session as both thrown
        // away and committed, inflating the number the cancel event exists to provide.
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "commit",
            requestId: "req1",
            payload: {
                replacements: [
                    { incomingId: `${kPageId}:0`, resultId: "result1" },
                ],
            },
        });
        expect(postJson).toHaveBeenCalledTimes(1);

        closeButton.click();
        // Sanity: nothing reported yet -- the outcome is still unknown.
        expect(cancelEvents()).toHaveLength(0);

        const onSuccess = postJson.mock.calls[0][2] as (r: {
            data: unknown;
        }) => void;
        onSuccess({
            data: {
                ok: true,
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

        expect(cancelEvents()).toHaveLength(0);
        // And the swap that landed on the page is still saved. Answering an ai-editor that has
        // gone away used to throw from inside postMessage, which skipped everything after it
        // in the finally block -- including this save, losing the user's picture.
        expect(postThatMightNavigate).toHaveBeenCalledWith(
            "common/saveChangesAndRethinkPageEvent",
        );
    });

    test("closing while a commit is in flight DOES report a cancel if the commit then fails", () => {
        // The other half: the session really did end with nothing kept, so it must still be
        // counted -- just later, once the answer is known.
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "commit",
            requestId: "req1",
            payload: {
                replacements: [
                    { incomingId: `${kPageId}:0`, resultId: "result1" },
                ],
            },
        });
        closeButton.click();
        expect(cancelEvents()).toHaveLength(0);

        const onError = postJson.mock.calls[0][3] as () => void;
        onError();

        expect(cancelEvents()).toHaveLength(1);
    });

    test("two overlapping commits: closing is not a cancel while either is outstanding", () => {
        // The ai-editor is free to send a second commit before the first is answered. With a flag
        // rather than a count, the first reply cleared it while the second was still in the air, so
        // closing then reported the session as thrown away with a commit still running.
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 0,
            expected: 0,
        });
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        const sendCommit = (requestId: string, slot: string) =>
            postFromEditor({
                channel: "bloom-ai-image-tools",
                type: "commit",
                requestId,
                payload: {
                    replacements: [
                        { incomingId: slot, resultId: "r" + requestId },
                    ],
                },
            });

        sendCommit("req1", "page2:0");
        sendCommit("req2", "page3:0");
        expect(postJson).toHaveBeenCalledTimes(2);

        // The FIRST one comes back, failing, while the second is still outstanding.
        (postJson.mock.calls[0][3] as () => void)();

        closeButton.click();
        expect(cancelEvents()).toHaveLength(0);

        // Once the second is answered too -- also with nothing applied -- the cancel is due.
        (postJson.mock.calls[1][3] as () => void)();
        expect(cancelEvents()).toHaveLength(1);
    });

    test("a reply path that throws still leaves the session reportable as abandoned", () => {
        // postJson chains .then(success).catch(error), so a throw inside the success callback runs
        // the error callback for the SAME request -- which is why both are exercised here. If the
        // outstanding-commit count were decremented by both, it would sit at -1 and "no commit
        // outstanding" would never be true again, so a session the user threw away would never be
        // counted.
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 0,
            expected: 0,
        });
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "commit",
            requestId: "req1",
            payload: {
                replacements: [{ incomingId: "page2:0", resultId: "r1" }],
            },
        });

        // Make the commit report throw, which is what escapes the success callback.
        trackEvent.mockImplementationOnce(() => {
            throw new Error("analytics blew up");
        });
        const onSuccess = postJson.mock.calls[0][2] as (r: {
            data: unknown;
        }) => void;
        expect(() =>
            onSuccess({
                data: {
                    ok: false,
                    results: [
                        {
                            incomingId: "page2:0",
                            ok: false,
                            isCurrentPage: false,
                        },
                    ],
                },
            }),
        ).toThrow();
        // ...so the error callback runs for the same request, as postJson would do.
        (postJson.mock.calls[0][3] as () => void)();

        closeButton.click();

        expect(cancelEvents()).toHaveLength(1);
    });

    test("of two overlapping commits, one failing and one succeeding is not also a cancel", () => {
        // The other ordering from the test above, and the one that was wrong: the user closes with
        // both commits outstanding, the FIRST comes back a failure, and the second then succeeds.
        // Reporting the cancel when the failure arrived would put one session in both the cancel
        // and the commit figures.
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 0,
            expected: 0,
        });
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        const sendCommit = (requestId: string, slot: string) =>
            postFromEditor({
                channel: "bloom-ai-image-tools",
                type: "commit",
                requestId,
                payload: {
                    replacements: [
                        { incomingId: slot, resultId: "r" + requestId },
                    ],
                },
            });

        sendCommit("req1", "page2:0");
        sendCommit("req2", "page3:0");
        closeButton.click();

        // The first fails...
        (postJson.mock.calls[0][3] as () => void)();
        expect(cancelEvents()).toHaveLength(0);

        // ...and the second puts its picture in the book.
        const onSuccess = postJson.mock.calls[1][2] as (r: {
            data: unknown;
        }) => void;
        onSuccess({
            data: {
                ok: true,
                results: [
                    { incomingId: "page3:0", ok: true, isCurrentPage: false },
                ],
            },
        });

        // Sanity: that commit really was reported as putting a picture in the book.
        expect(
            trackEvent.mock.calls.filter(
                (call) => call[0] === "AI Editor Commit",
            ).length,
        ).toBeGreaterThan(0);
        expect(cancelEvents()).toHaveLength(0);
    });
    test("a commit answered after the ai-editor was reopened leaves the new overlay alone", () => {
        // The old session's success path calls its own cleanup, which tears down "the" overlay by
        // id and deletes the cleanup hook on the window -- both of which belong to the NEW session
        // by then. Without an idempotence guard the editor the user is looking at disappears.
        const first = openAgainstABookWithOneImage();

        first.postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "commit",
            requestId: "req1",
            payload: {
                replacements: [
                    { incomingId: `${kPageId}:0`, resultId: "result1" },
                ],
            },
        });
        const onSuccess = postJson.mock.calls[0][2] as (r: {
            data: unknown;
        }) => void;

        first.closeButton.click();
        // The helper expects a fresh launch, and this is the second in one test.
        post.mockClear();
        openAgainstABookWithOneImage();
        // Sanity: the new session is up and is the one the window would tear down.
        expect(document.getElementById("ai-editor-overlay")).not.toBeNull();

        onSuccess({
            data: {
                ok: true,
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

        expect(document.getElementById("ai-editor-overlay")).not.toBeNull();
        expect(
            (window as Window & { __bloomAiImageEditorCleanup?: () => void })
                .__bloomAiImageEditorCleanup,
        ).toBeTypeOf("function");
    });
    test("a cancel is reported at most once", () => {
        const { closeButton } = openAgainstABookWithOneImage();

        closeButton.click();
        expect(cancelEvents()).toHaveLength(1);

        // The close box is gone with the overlay, but the cleanup hook survives on the window
        // for a relaunch to call; calling it again must not report a second cancel.
        (
            window as Window & { __bloomAiImageEditorCleanup?: () => void }
        ).__bloomAiImageEditorCleanup?.();

        expect(cancelEvents()).toHaveLength(1);
    });
    test("a successful commit is not reported as a cancel", () => {
        const { postFromEditor } = openAgainstABookWithOneImage();

        commitAndReplyFromHost(postFromEditor, true);

        // Sanity: the commit really did close the overlay, so this is not just a session
        // that never ended.
        expect(document.getElementById("ai-editor-overlay")).toBeNull();
        expect(cancelEvents()).toHaveLength(0);
    });
});

// "AI Editor Commit" and the per-picture "Change Picture" events are reported from the overlay
// rather than from C#, because C# cannot know whether a picture on the page being edited actually
// got swapped in -- it only stages those. These tests are what makes that worth having: they pin
// that a swap the page frame failed to make is NOT counted as a picture that reached the book.
describe("aiEditorOverlay: reporting what a commit achieved", () => {
    const commitEvents = () =>
        trackEvent.mock.calls.filter((call) => call[0] === "AI Editor Commit");

    // Sends a commit for the given replacements and answers it with C#'s reply.
    const commitAndReply = (
        postFromEditor: (data: unknown) => void,
        replacements: Array<{
            incomingId: string;
            resultId?: string;
            sourceUrl?: string;
        }>,
        results: Array<Record<string, unknown>>,
    ) => {
        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "commit",
            requestId: "req1",
            payload: { replacements },
        });
        expect(postJson).toHaveBeenCalledTimes(1);
        const onSuccess = postJson.mock.calls[0][2] as (r: {
            data: unknown;
        }) => void;
        onSuccess({ data: { ok: true, results } });
    };

    const currentPageResult = (ordinal: number) => ({
        incomingId: `${kPageId}:${ordinal}`,
        ok: true,
        isCurrentPage: true,
        oldSrc: kImageFile,
        newSrc: `ai-image${ordinal}.png`,
    });

    test("a picture the page frame could not swap in is not counted as applied", () => {
        // The case the move exists for. C# staged this replacement and would have called it
        // applied; the live page is where it actually failed.
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 0,
            expected: 1,
        });
        const { postFromEditor } = openAgainstABookWithOneImage();

        commitAndReply(
            postFromEditor,
            [{ incomingId: `${kPageId}:0`, resultId: "result1" }],
            [currentPageResult(0)],
        );

        expect(commitEvents()).toHaveLength(1);
        expect(commitEvents()[0][1]).toMatchObject({
            replacementCount: 1,
            appliedCount: 0,
            failedCount: 1,
        });
        // And no picture is added to the where-do-pictures-come-from breakdown.
        expect(trackChangePicture).not.toHaveBeenCalled();
    });

    test("applied adds up C#'s off-page successes and what the page frame landed", () => {
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 1,
            expected: 1,
        });
        const { postFromEditor } = openAgainstABookWithOneImage();

        commitAndReply(
            postFromEditor,
            [
                { incomingId: `${kPageId}:0`, resultId: "result1" },
                { incomingId: "page2:0", resultId: "result2" },
                { incomingId: "page3:0", resultId: "result3" },
            ],
            [
                currentPageResult(0),
                // C# applied and saved this one itself.
                { incomingId: "page2:0", ok: true, isCurrentPage: false },
                // And could not do this one at all.
                { incomingId: "page3:0", ok: false, isCurrentPage: false },
            ],
        );

        expect(commitEvents()[0][1]).toMatchObject({
            replacementCount: 3,
            appliedCount: 2,
            failedCount: 1,
        });
        // One per picture that reached the book, in the same vocabulary as the other routes.
        expect(trackChangePicture).toHaveBeenCalledTimes(2);
        expect(trackChangePicture).toHaveBeenCalledWith(
            "AI editor",
            "ai-editor",
        );
    });

    test("generated and reused come from what the editor sent", () => {
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 1,
            expected: 1,
        });
        const { postFromEditor } = openAgainstABookWithOneImage();

        commitAndReply(
            postFromEditor,
            [
                // A newly generated image: the editor gives it a result id.
                { incomingId: `${kPageId}:0`, resultId: "result1" },
                // One the user reused from an image already in the book.
                {
                    incomingId: "page2:0",
                    sourceUrl: "http://host/book/existing.png",
                },
            ],
            [
                currentPageResult(0),
                { incomingId: "page2:0", ok: true, isCurrentPage: false },
            ],
        );

        expect(commitEvents()[0][1]).toMatchObject({
            generatedCount: 1,
            reusedCount: 1,
        });
    });

    test("a commit is reported at most once, even if the reply path also errors", () => {
        // postJson chains .then(success).catch(error), so anything escaping the success callback
        // runs the error callback too -- which reports as well. One commit must not be counted
        // twice, nor its pictures added twice to the picture-source breakdown.
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 1,
            expected: 1,
        });
        const { postFromEditor } = openAgainstABookWithOneImage();

        commitAndReply(
            postFromEditor,
            [{ incomingId: `${kPageId}:0`, resultId: "result1" }],
            [currentPageResult(0)],
        );
        expect(commitEvents()).toHaveLength(1);
        expect(trackChangePicture).toHaveBeenCalledTimes(1);

        // Now the error callback runs as well, as it would if anything threw on the way out.
        const onError = postJson.mock.calls[0][3] as () => void;
        onError();

        expect(commitEvents()).toHaveLength(1);
        expect(trackChangePicture).toHaveBeenCalledTimes(1);
    });
    test("a session that got some pictures into the book is not also counted as thrown away", () => {
        // A commit can succeed for one picture and fail for another. The overlay stays open, and
        // the user closes it -- but their AI work was not thrown away, so a cancel here would count
        // the same session as both saved and discarded.
        applyAiImageEditorReplacements.mockReturnValue({
            applied: 0,
            expected: 1,
        });
        const { closeButton, postFromEditor } = openAgainstABookWithOneImage();

        commitAndReply(
            postFromEditor,
            [
                { incomingId: `${kPageId}:0`, resultId: "result1" },
                { incomingId: "page2:0", resultId: "result2" },
            ],
            [
                // The page frame could not swap this one in...
                currentPageResult(0),
                // ...but C# applied and saved this one itself.
                { incomingId: "page2:0", ok: true, isCurrentPage: false },
            ],
        );
        // Sanity: one picture did reach the book, and the overlay is still up.
        expect(commitEvents()[0][1]).toMatchObject({ appliedCount: 1 });
        expect(document.getElementById("ai-editor-overlay")).not.toBeNull();

        closeButton.click();

        expect(
            trackEvent.mock.calls.filter(
                (call) => call[0] === "AI Editor Cancel",
            ),
        ).toHaveLength(0);
    });
    test("a commit whose request fails reports that nothing landed", () => {
        const { postFromEditor } = openAgainstABookWithOneImage();

        postFromEditor({
            channel: "bloom-ai-image-tools",
            type: "commit",
            requestId: "req1",
            payload: {
                replacements: [
                    { incomingId: `${kPageId}:0`, resultId: "result1" },
                ],
            },
        });
        // Sanity: nothing reported until the request is answered one way or the other.
        expect(commitEvents()).toHaveLength(0);

        const onError = postJson.mock.calls[0][3] as () => void;
        onError();

        expect(commitEvents()[0][1]).toMatchObject({
            replacementCount: 1,
            appliedCount: 0,
            failedCount: 1,
        });
        expect(trackChangePicture).not.toHaveBeenCalled();
    });
});
