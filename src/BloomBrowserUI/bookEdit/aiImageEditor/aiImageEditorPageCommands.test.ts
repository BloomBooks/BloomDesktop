import { beforeEach, describe, expect, test, vi } from "vitest";

// Tests for the page-frame half of the AI Image Editor integration: the menu command, and
// applying a commit's current-page swaps to the live DOM.
//
// The menu command deliberately does NOT open the editor. Everything C# tells the editor
// about the book is read from the SAVED book DOM, so an image the user just added — which
// lives only in the live page — wouldn't be there (BL-16682). The command therefore reports
// which slot was clicked and asks C# to save the page first; C# opens the overlay itself, in
// the top window.
//
// A slot is an image container, and its index among the page's image containers is its whole
// identity. C# numbers the same containers on the saved page (SelectImageSlotsOnPage), so an
// index means the same thing on both sides. These tests are mostly about that agreement.

const postJson = vi.fn();
const changeImageByElement = vi.fn();
const setActiveElementToClosest = vi.fn();

vi.mock("../../utils/bloomApi", () => ({
    postJson: (...args: unknown[]) => postJson(...args),
}));

vi.mock("../js/bloomEditing", () => ({
    changeImageByElement: (...args: unknown[]) => changeImageByElement(...args),
}));

vi.mock("../js/canvasElementManager/CanvasElementManager", () => ({
    theOneCanvasElementManager: {
        setActiveElementToClosest: (...args: unknown[]) =>
            setActiveElementToClosest(...args),
    },
}));

vi.mock("../js/bloomImages", () => ({
    kImageContainerClass: "bloom-imageContainer",
}));

import {
    applyAiImageEditorReplacements,
    launchAiImageEditor,
} from "./aiImageEditorPageCommands";

const kPageId = "page1";

// A page whose slots hold the given files, in order. Each slot is an image container inside
// a canvas element, which is how a real page holds a picture.
const makePageWithImages = (...fileNames: string[]) => {
    document.body.innerHTML = `
        <div class="bloom-page" id="${kPageId}">
            ${fileNames
                .map(
                    (name) =>
                        `<div class="bloom-canvas-element"><div class="bloom-imageContainer"><img src="${name}" /></div></div>`,
                )
                .join("")}
        </div>`;
    return Array.from(document.querySelectorAll("img")) as HTMLImageElement[];
};

const containers = () =>
    Array.from(
        document.querySelectorAll(".bloom-imageContainer"),
    ) as HTMLElement[];

// A current-page commit result for slot `ordinal`, replacing `oldSrc` with `newSrc`.
const currentPageResult = (
    ordinal: number,
    oldSrc: string,
    newSrc: string,
) => ({
    incomingId: `${kPageId}:${ordinal}`,
    ok: true,
    isCurrentPage: true,
    oldSrc,
    newSrc,
    copyright: "",
    creator: "",
    license: "",
});

describe("aiImageEditorPageCommands: the menu command", () => {
    beforeEach(() => {
        postJson.mockClear();
        document.body.innerHTML = "";
    });

    test("asks C# to save the page rather than opening the editor", () => {
        const [img] = makePageWithImages("old.png");

        launchAiImageEditor(img, undefined);

        expect(postJson).toHaveBeenCalledTimes(1);
        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            // Only plain data travels: the save reloads this frame, so a live element
            // reference could not survive the round trip. C# adds the page id.
            slotIndex: 0,
        });
    });

    test("numbers the slot that was clicked, not the picture it shows (BL-16744)", () => {
        // Every empty slot shows placeHolder.png, so nothing about the picture could say
        // which slot the user clicked. The index can, and it says so whatever the pictures
        // are: here the same file twice.
        const [first, second] = makePageWithImages(
            "placeHolder.png",
            "placeHolder.png",
        );

        launchAiImageEditor(second, undefined);
        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            slotIndex: 1,
        });

        // Sanity: the other slot of the same pair is a different index.
        postJson.mockClear();
        launchAiImageEditor(first, undefined);
        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            slotIndex: 0,
        });
    });

    test("the clicked container may be given instead of the img", () => {
        // canvasControlRegistry passes both when it has both. Either must number the same
        // slot, because they are the same slot.
        makePageWithImages("a.png", "b.png");
        const [, secondContainer] = containers();

        launchAiImageEditor(
            secondContainer.querySelector("img") as HTMLImageElement,
            secondContainer,
        );

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            slotIndex: 1,
        });
    });

    test("a branding image does not shift the index", () => {
        // Branding, license and QR-code images are not in image containers, so they are not
        // slots at all — which is why neither side needs a list of them. C# will not offer
        // one and cannot overwrite one.
        document.body.innerHTML = `
            <div class="bloom-page" id="${kPageId}">
                <img class="branding" src="placeHolder.png" />
                <div class="bloom-imageContainer"><img src="placeHolder.png" /></div>
                <div class="bloom-imageContainer"><img src="placeHolder.png" /></div>
            </div>`;
        const images = Array.from(
            document.querySelectorAll("img"),
        ) as HTMLImageElement[];

        launchAiImageEditor(images[2], undefined);

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            slotIndex: 1,
        });
    });

    test("a control Bloom injects into the live page does not shift the index", () => {
        // Injected controls live in the live page only — the save strips them — so the
        // saved page C# numbers has none of them.
        document.body.innerHTML = `
            <div class="bloom-page" id="${kPageId}">
                <div class="bloom-ui"><div class="bloom-imageContainer"><img src="icon.png" /></div></div>
                <div class="bloom-imageContainer"><img src="placeHolder.png" /></div>
                <div class="bloom-imageContainer"><img src="placeHolder.png" /></div>
            </div>`;
        const images = Array.from(
            document.querySelectorAll("img"),
        ) as HTMLImageElement[];

        launchAiImageEditor(images[2], undefined);

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            slotIndex: 1,
        });
    });

    test("every slot counts, whatever picture it shows", () => {
        // The index counts slots, not pictures of one name. A slot C# declines to offer —
        // an svg it cannot open, say — still holds its place in the numbering on both
        // sides, which is what lets this side number slots without knowing C#'s rules.
        const images = makePageWithImages(
            "placeHolder.png",
            "photo.svg",
            "placeHolder.png",
        );

        launchAiImageEditor(images[2], undefined);

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            slotIndex: 2,
        });
    });
});

describe("aiImageEditorPageCommands: applying current-page replacements", () => {
    beforeEach(() => {
        changeImageByElement.mockClear();
        setActiveElementToClosest.mockClear();
        document.body.innerHTML = "";
    });

    test("swaps the image of the named slot, undoably, and reports it applied", () => {
        const [img] = makePageWithImages("old.png");

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(0, "old.png", "ai-image1.png"),
        ]);

        expect(outcome).toEqual({ applied: 1, expected: 1 });
        expect(changeImageByElement).toHaveBeenCalledTimes(1);
        // The img, not the container: changeImageInfo paints a background image on anything
        // that is not an <img>, which would leave the img showing the old picture.
        expect(changeImageByElement.mock.calls[0][0]).toBe(img);
        expect(changeImageByElement.mock.calls[0][1]).toMatchObject({
            src: "ai-image1.png",
            // Registers an image undo, like a pasted image; that is why the overlay must
            // not save afterwards (the reload would discard the undo stack).
            undoable: "true",
        });
        // The swapped slot becomes the active element, or canUndoImageOperation would
        // refuse to offer the undo until the user clicked the image.
        expect(setActiveElementToClosest).toHaveBeenCalledWith(img);
    });

    test("a slot with no img of its own is swapped on the container", () => {
        // A slot can wear its picture as a background image instead of holding an img.
        document.body.innerHTML = `
            <div class="bloom-page" id="${kPageId}">
                <div class="bloom-imageContainer" style="background-image:url('old.png')"></div>
            </div>`;
        const [container] = containers();

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(0, "old.png", "ai-image1.png"),
        ]);

        expect(outcome).toEqual({ applied: 1, expected: 1 });
        expect(changeImageByElement.mock.calls[0][0]).toBe(container);
    });

    test("a retry re-targets the same slot, though it no longer shows what C# read", () => {
        // After a partial failure the overlay stays up and nothing was saved, so a retry's
        // oldSrc (read from the saved page) still names the pre-swap file. The index does
        // not care, which is the point: nothing here compares pictures.
        const images = makePageWithImages(
            "placeHolder.png",
            "placeHolder.png",
            "placeHolder.png",
        );
        applyAiImageEditorReplacements([
            currentPageResult(2, "placeHolder.png", "ai-image1.png"),
        ]);
        images[2].setAttribute("src", "ai-image1.png");
        changeImageByElement.mockClear();

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(2, "placeHolder.png", "ai-image2.png"),
        ]);

        expect(outcome).toEqual({ applied: 1, expected: 1 });
        expect(changeImageByElement.mock.calls[0][0]).toBe(images[2]);
    });

    test("a lone swap lands on the slot its ordinal names (BL-16744)", () => {
        // Every empty slot shows placeHolder.png. A commit for the third of them must not
        // land on the first, which is where a filename-only match always put it.
        const images = makePageWithImages(
            "placeHolder.png",
            "placeHolder.png",
            "placeHolder.png",
        );

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(2, "placeHolder.png", "ai-image1.png"),
        ]);

        expect(outcome).toEqual({ applied: 1, expected: 1 });
        expect(changeImageByElement).toHaveBeenCalledTimes(1);
        expect(changeImageByElement.mock.calls[0][0]).toBe(images[2]);
    });

    test("a control Bloom injects into the live page does not shift the ordinal", () => {
        // Injected controls are in the live page only; the ordinal counts the saved page's
        // slots, which have none of them.
        document.body.innerHTML = `
            <div class="bloom-page" id="${kPageId}">
                <div class="bloom-ui"><div class="bloom-imageContainer"><img src="icon.png" /></div></div>
                <div class="bloom-imageContainer"><img src="placeHolder.png" /></div>
                <div class="bloom-imageContainer"><img src="placeHolder.png" /></div>
            </div>`;
        const images = Array.from(
            document.querySelectorAll("img"),
        ) as HTMLImageElement[];

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(1, "placeHolder.png", "ai-image1.png"),
        ]);

        expect(outcome).toEqual({ applied: 1, expected: 1 });
        expect(changeImageByElement.mock.calls[0][0]).toBe(images[2]);
    });

    test("ignores results for other pages and results that failed", () => {
        makePageWithImages("old.png");

        const outcome = applyAiImageEditorReplacements([
            {
                ...currentPageResult(0, "old.png", "a.png"),
                isCurrentPage: false,
            },
            { ...currentPageResult(0, "old.png", "b.png"), ok: false },
        ]);

        expect(outcome).toEqual({ applied: 0, expected: 0 });
        expect(changeImageByElement).not.toHaveBeenCalled();
    });

    test("reports a shortfall when the page has no such slot", () => {
        // The caller turns applied < expected into "Only N of M ... could be updated", so
        // this has to be counted honestly rather than reported as success.
        makePageWithImages("old.png");

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(0, "old.png", "ai-image1.png"),
            currentPageResult(1, "notOnThisPage.png", "ai-image2.png"),
        ]);

        expect(outcome).toEqual({ applied: 1, expected: 2 });
        expect(changeImageByElement).toHaveBeenCalledTimes(1);
    });

    test("a swap that throws still reports the swaps that landed", () => {
        // Whatever landed is in the live DOM and nothing else will persist it, so the
        // caller must learn about it in order to save; losing that count would lose the
        // user's images.
        makePageWithImages("first.png", "second.png");
        changeImageByElement.mockImplementationOnce(() => undefined);
        changeImageByElement.mockImplementationOnce(() => {
            throw new Error("kaboom");
        });

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(0, "first.png", "ai-image1.png"),
            currentPageResult(1, "second.png", "ai-image2.png"),
        ]);

        expect(outcome.applied).toBe(1);
        expect(outcome.expected).toBe(2);
        expect(outcome.error).toContain("kaboom");
    });
});
