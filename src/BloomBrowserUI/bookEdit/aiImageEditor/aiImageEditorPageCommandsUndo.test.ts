// applyAiImageEditorReplacements and the page history, with a real history and Bloom's real
// doTransactionUntilPageSettles and changeImageByElement: the swaps that one commit makes on this
// page are one undo step, and so are the fits of their canvas elements to the new pictures, which
// come after the swaps, once each picture has loaded.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

// The page history reaches the history through the page frame's exports. There is no toolbox.
vi.mock("../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history ? { pageHistory: () => frames.history } : null,
    getToolboxBundleExports: () => null,
}));

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. The canvas element
// manager only tells it, when it is made, which elements' tails may overlap.
vi.mock("comicaljs", () => ({
    Bubble: vi.fn(),
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
    },
}));

// These imports come after the vi.mock calls above, so that what they pull in gets the stand-ins.
import { applyAiImageEditorReplacements } from "./aiImageEditorPageCommands";
import {
    addRequestPageContentDelay,
    kChangePictureUndoL10nKey,
    removeRequestPageContentDelay,
    whenNoRequestPageContentDelays,
} from "../js/bloomEditing";
import {
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "../js/canvasElementManager/CanvasElementManager";
import { addToLatestTransaction } from "../pageHistory/pageHistoryAccess";
import { createPageHistory } from "../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../pageHistory/editTimeMarkup";

const kPageId = "page1";
const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kFitDelayId = "fitToNewPicture";

let page: HTMLElement;
let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn<(whatChanged: string) => void>>;
// The fits that the canvas element manager is holding until "the picture loads".
let pendingFits: (() => void)[];
// False for pictures whose canvas elements need no fit, as when the real manager finds nothing
// to wait for.
let picturesNeedFits: boolean;

beforeEach(async () => {
    document.body.innerHTML = `
        <div class="bloom-page" id="${kPageId}">
            <div class="bloom-canvas">
                <div class="bloom-canvas-element" style="width: 100px;"><div class="bloom-imageContainer"><img src="old0.png"></div></div>
                <div class="bloom-canvas-element" style="width: 100px;"><div class="bloom-imageContainer"><img src="old1.png"></div></div>
                <div id="other">before</div>
            </div>
        </div>`;
    page = document.querySelector<HTMLElement>(".bloom-page")!;
    pendingFits = [];
    picturesNeedFits = true;
    initializeCanvasElementManager();
    // jsdom loads no pictures, so the real fit of a canvas element to its new picture never
    // finishes. This does what the real one does (waitForImageAndAdjustContainerAspectRatio): it
    // holds a page content delay until the picture loads, then fits the box in
    // addToLatestTransaction.
    vi.spyOn(
        theOneCanvasElementManager,
        "updateCanvasElementForChangedImage",
    ).mockImplementation((img: HTMLElement) => {
        if (!picturesNeedFits) return;
        addRequestPageContentDelay(kFitDelayId);
        pendingFits.push(() => {
            try {
                addToLatestTransaction(() => {
                    img.closest<HTMLElement>(
                        ".bloom-canvas-element",
                    )!.style.width = "160px";
                });
            } finally {
                removeRequestPageContentDelay(kFitDelayId);
            }
        });
    });
    // Selecting the canvas element is not what these tests are about.
    vi.spyOn(
        theOneCanvasElementManager,
        "setActiveElementToClosest",
    ).mockImplementation(() => undefined);
    onUnmarkedChange = vi.fn<(whatChanged: string) => void>();
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    frames.history = history;
    // An earlier step, so that an undo that went too far would show.
    await history.doTransaction(kEarlierKey, () => {
        document.getElementById("other")!.textContent = "after";
    });
});

afterEach(() => {
    vi.restoreAllMocks();
    history.dispose();
    frames.history = undefined;
    document.body.innerHTML = "";
});

/** A commit's result for the picture at this ordinal on the current page. */
function currentPageResult(ordinal: number, oldSrc: string, newSrc: string) {
    return {
        incomingId: `${kPageId}:${ordinal}`,
        ok: true,
        isCurrentPage: true,
        oldSrc,
        newSrc,
        copyright: "",
        creator: "",
        license: "",
    };
}

/** The src of each picture on the page. */
function sources(): (string | null)[] {
    return Array.from(page.querySelectorAll("img")).map((img) =>
        img.getAttribute("src"),
    );
}

/** The width of each canvas element on the page. */
function widths(): string[] {
    return Array.from(
        page.querySelectorAll<HTMLElement>(".bloom-canvas-element"),
    ).map((element) => element.style.width);
}

describe("applyAiImageEditorReplacements and the page history", () => {
    it("makes the swaps of one commit and the fits that follow them one undo step", async () => {
        const outcome = applyAiImageEditorReplacements([
            currentPageResult(0, "old0.png", "ai-image1.png"),
            currentPageResult(1, "old1.png", "ai-image2.png"),
        ]);
        expect(outcome).toEqual({ applied: 2, expected: 2 });
        expect(sources()).toEqual(["ai-image1.png", "ai-image2.png"]);
        // The step stays open while the fits wait for the pictures.
        expect(pendingFits.length).toBe(2);
        expect(history.hasOpenTransaction).toBe(true);

        pendingFits.forEach((fit) => fit());
        await whenNoRequestPageContentDelays();
        await Promise.resolve();

        expect(widths()).toEqual(["160px", "160px"]);
        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kChangePictureUndoL10nKey);

        await history.undo();

        expect(sources()).toEqual(["old0.png", "old1.png"]);
        expect(widths()).toEqual(["100px", "100px"]);
        // One undo took back both swaps and both fits, and nothing more.
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(document.getElementById("other")!.textContent).toBe("after");

        await history.redo();

        expect(sources()).toEqual(["ai-image1.png", "ai-image2.png"]);
        expect(widths()).toEqual(["160px", "160px"]);
        expect(history.undoDescriptionL10nKey).toBe(kChangePictureUndoL10nKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("makes the swaps of one commit one undo step when there is no fit to wait for", async () => {
        // Without a fit holding the first swap's step open, nothing but the transaction around
        // the whole commit keeps the second swap in the same step.
        picturesNeedFits = false;

        applyAiImageEditorReplacements([
            currentPageResult(0, "old0.png", "ai-image1.png"),
            currentPageResult(1, "old1.png", "ai-image2.png"),
        ]);

        expect(history.hasOpenTransaction).toBe(false);
        expect(sources()).toEqual(["ai-image1.png", "ai-image2.png"]);
        await history.undo();
        expect(sources()).toEqual(["old0.png", "old1.png"]);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });
});
