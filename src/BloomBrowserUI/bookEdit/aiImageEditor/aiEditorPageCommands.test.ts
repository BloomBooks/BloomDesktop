import { beforeEach, describe, expect, test, vi } from "vitest";

// Tests for the page-frame half of the AI Image Editor integration: the menu command, and
// applying a commit's current-page swaps to the live DOM.
//
// The menu command deliberately does NOT open the editor. Everything C# tells the editor
// about the book is read from the SAVED book DOM, so an image the user just added — which
// lives only in the live page — wouldn't be there (BL-16682). The command therefore reports
// the clicked image and asks C# to save the page first; C# opens the overlay itself, in the
// top window.

const postJson = vi.fn();
const changeImageByElement = vi.fn();

vi.mock("../../utils/bloomApi", () => ({
    postJson: (...args: unknown[]) => postJson(...args),
}));

vi.mock("../js/bloomEditing", () => ({
    changeImageByElement: (...args: unknown[]) => changeImageByElement(...args),
}));

vi.mock("../js/bloomImages", () => ({
    getImageUrlFromImageContainer: (container: HTMLElement) =>
        container.getAttribute("data-url") ?? "",
    // The matcher compares filenames off the live elements; in the real thing this reads
    // an <img src> or a background-image url, which for our fixture is just the src.
    GetRawImageUrl: (element: HTMLElement) => element.getAttribute("src") ?? "",
}));

import {
    applyAiImageEditorReplacements,
    launchAiImageEditor,
} from "./aiEditorPageCommands";

const kPageId = "page1";

const makePageWithImages = (...fileNames: string[]) => {
    document.body.innerHTML = `
        <div class="bloom-page" id="${kPageId}">
            ${fileNames
                .map(
                    (name) =>
                        `<div class="bloom-canvas-element"><img src="${name}" /></div>`,
                )
                .join("")}
        </div>`;
    return Array.from(document.querySelectorAll("img")) as HTMLImageElement[];
};

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

describe("aiEditorPageCommands: the menu command", () => {
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
            imageFileName: "old.png",
            sameNameOrdinal: 0,
        });
    });

    test("prefers the image container's url over the img src", () => {
        // An image container's url is the authoritative one (it may be a background-image
        // rather than an <img src>), which is why the command asks for it when there is one.
        makePageWithImages("ignored.png");
        const container = document.querySelector(
            ".bloom-canvas-element",
        ) as HTMLElement;
        container.setAttribute("data-url", "fromContainer.png");
        const img = document.querySelector("img") as HTMLImageElement;

        launchAiImageEditor(img, container);

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            imageFileName: "fromContainer.png",
            sameNameOrdinal: 0,
        });
    });

    test("counts the same-named slots ahead of the clicked one (BL-16744)", () => {
        // Every empty slot shows placeHolder.png, so the file name alone cannot say which
        // one the user clicked. Without the count the overlay picked the first, and the
        // image the user made for the second slot landed in the first.
        const [first, second] = makePageWithImages(
            "placeHolder.png",
            "placeHolder.png",
        );

        launchAiImageEditor(second, undefined);

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            imageFileName: "placeHolder.png",
            sameNameOrdinal: 1,
        });

        // Sanity: the first slot of the same pair still counts as none ahead of it.
        postJson.mockClear();
        launchAiImageEditor(first, undefined);
        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            imageFileName: "placeHolder.png",
            sameNameOrdinal: 0,
        });
    });

    test("a branding slot showing the same placeholder does not shift the count", () => {
        // C# never offers a branding, license, or QR slot to the editor, but an empty one of
        // those shows placeHolder.png too. Counting it would put the count one ahead of the
        // list C# sent, and the overlay would fall back to the first empty slot.
        document.body.innerHTML = `
            <div class="bloom-page" id="${kPageId}">
                <div class="bloom-canvas-element"><img class="branding" src="placeHolder.png" /></div>
                <div class="bloom-canvas-element"><img src="placeHolder.png" /></div>
                <div class="bloom-canvas-element"><img src="placeHolder.png" /></div>
            </div>`;
        const images = Array.from(
            document.querySelectorAll("img"),
        ) as HTMLImageElement[];

        launchAiImageEditor(images[2], undefined);

        // The branding slot is not in C#'s list, so the clicked slot is its SECOND entry.
        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            imageFileName: "placeHolder.png",
            sameNameOrdinal: 1,
        });
    });

    test("a control Bloom injects into the live page does not shift the count", () => {
        // Injected controls are in the live page only; C# read the saved book, which has
        // none of them.
        document.body.innerHTML = `
            <div class="bloom-page" id="${kPageId}">
                <div class="bloom-ui"><img src="placeHolder.png" /></div>
                <div class="bloom-canvas-element"><img src="placeHolder.png" /></div>
                <div class="bloom-canvas-element"><img src="placeHolder.png" /></div>
            </div>`;
        const images = Array.from(
            document.querySelectorAll("img"),
        ) as HTMLImageElement[];

        launchAiImageEditor(images[2], undefined);

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            imageFileName: "placeHolder.png",
            sameNameOrdinal: 1,
        });
    });

    test("a differently-named picture in between does not shift the count", () => {
        // The count runs over the same-named slots only, which is what keeps it immune to
        // the extra images Bloom injects into the live page and to the pictures C# leaves
        // out of the book image list.
        const images = makePageWithImages(
            "placeHolder.png",
            "photo.png",
            "placeHolder.png",
        );

        launchAiImageEditor(images[2], undefined);

        expect(postJson).toHaveBeenCalledWith("aiImageEditor/saveThenLaunch", {
            imageFileName: "placeHolder.png",
            sameNameOrdinal: 1,
        });
    });
});

describe("aiEditorPageCommands: applying current-page replacements", () => {
    beforeEach(() => {
        changeImageByElement.mockClear();
        document.body.innerHTML = "";
    });

    test("swaps the matching image and reports it applied", () => {
        const [img] = makePageWithImages("old.png");

        const outcome = applyAiImageEditorReplacements([
            currentPageResult(0, "old.png", "ai-image1.png"),
        ]);

        expect(outcome).toEqual({ applied: 1, expected: 1 });
        expect(changeImageByElement).toHaveBeenCalledTimes(1);
        expect(changeImageByElement.mock.calls[0][0]).toBe(img);
        expect(changeImageByElement.mock.calls[0][1]).toMatchObject({
            src: "ai-image1.png",
            undoable: "false",
        });
    });

    test("a lone swap lands on the slot its ordinal names, not the first same-named slot (BL-16744)", () => {
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
        // holders, which have none of them.
        document.body.innerHTML = `
            <div class="bloom-page" id="${kPageId}">
                <div class="bloom-ui"><img src="placeHolder.png" /></div>
                <div class="bloom-canvas-element"><img src="placeHolder.png" /></div>
                <div class="bloom-canvas-element"><img src="placeHolder.png" /></div>
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

    test("reports a shortfall when a slot cannot be matched", () => {
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
