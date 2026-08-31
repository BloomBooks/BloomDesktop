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
} from "./aiImageEditorPageCommands";

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
            // Only the file name travels: the save reloads this frame, so a live element
            // reference could not survive the round trip. C# adds the page id.
            imageFileName: "old.png",
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
        });
    });
});

describe("aiImageEditorPageCommands: applying current-page replacements", () => {
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
