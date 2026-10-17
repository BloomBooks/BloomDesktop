// Tests that resizing a picture by a corner leaves its box fitted to the picture inside the
// resize's own undo step, so that selecting the picture afterwards writes nothing: no step of its
// own, and no change that undo and redo do not put back. Uses a real page history and the real
// edit-time markup filter, wired to the manager by listenToPageHistory as in the Edit tab, and
// drives the drag through the control frame's corner handle.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Selecting and resizing
// an element only asks Comical to activate it, draw the bubbles again, find the element's
// family, and read its bubble style and tails; these canvas elements have none.
vi.mock("comicaljs", () => ({
    Bubble: class {
        public static getBubbleSpec() {
            return { style: "none", tails: [] };
        }
        public getBubbleSpec() {
            return { style: "none", tails: [] };
        }
    },
    Node: vi.fn(),
    Parser: vi.fn(),
    BubbleSpec: {},
    TailSpec: {},
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: () => {},
        startEditing: () => {},
        findAncestors: () => [],
    },
}));

// In the Edit tab, code in the page frame finds the canvas element manager and the page history
// through the page frame's bundle exports; give it this test's.
const pageFrame = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("../workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../workspaceFrames")>()),
    getEditablePageBundleExports: () => ({
        getTheOneCanvasElementManager: () => theOneCanvasElementManager,
        // Like the real one, this throws when no history has been started.
        pageHistory: () => {
            if (!pageFrame.history) throw new Error("No page history");
            return pageFrame.history;
        },
    }),
}));

// These imports deliberately come after the vi.mock calls above, so that the module graph they
// pull in gets the stubbed modules.
import {
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "./CanvasElementManager";
import { listenToPageHistory } from "../bloomEditing";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import {
    formatUndoDescription,
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";

const kResizeDescription = "EditTab.Undo.Resize";
// The picture's natural size. Its box starts out fitted to it: 133.2 / 100 = 333 / 250.
const kNaturalWidth = 333;
const kNaturalHeight = 250;

let page: HTMLElement;
let history: PageHistory;

// jsdom does no layout. Give an element the measurements a browser would: clientWidth and
// offsetLeft and the like are its style's lengths rounded to whole pixels.
function simulateLayout(element: HTMLElement): void {
    const rounded = (value: string) => Math.round(parseFloat(value) || 0);
    Object.defineProperties(element, {
        clientWidth: { get: () => rounded(element.style.width) },
        clientHeight: { get: () => rounded(element.style.height) },
        offsetLeft: { get: () => rounded(element.style.left) },
        offsetTop: { get: () => rounded(element.style.top) },
    });
}

beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-canvas bloom-has-canvas-element" id="canvas">
                <div class="bloom-canvas-element bloom-backgroundImage" id="background" style="left: 0px; top: 0px; width: 400px; height: 300px;">
                    <div class="bloom-imageContainer"><img src="placeHolder.png" alt=""></div>
                </div>
                <div class="bloom-canvas-element" id="picture" style="left: 20.4px; top: 20.6px; width: 133.2px; height: 100px;">
                    <div class="bloom-imageContainer"><img id="pictureImg" src="bird.png" alt=""></div>
                </div>
                <div class="bloom-canvas-element" id="ce1" style="left: 250px; top: 200px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" lang="en"><p>One</p></div>
                    </div>
                </div>
            </div>
        </div>`;
    page = document.getElementById("page")!;
    simulateLayout(document.getElementById("picture")!);
    simulateLayout(document.getElementById("ce1")!);
    Object.defineProperties(document.getElementById("pictureImg")!, {
        naturalWidth: { get: () => kNaturalWidth },
        naturalHeight: { get: () => kNaturalHeight },
    });
    initializeCanvasElementManager();
    theOneCanvasElementManager.isCanvasElementEditingOn = true;
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
    });
    pageFrame.history = history;
    listenToPageHistory(history);
    history.doOutsideHistory(() =>
        theOneCanvasElementManager.setActiveElement(undefined),
    );
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
});

// A MutationObserver delivers its records in a microtask, so by the time this resolves the
// history has every change made before the call.
async function letObserverDeliver(): Promise<void> {
    await Promise.resolve();
}

// Run fn as Bloom runs the handlers of a click or a drag: inside the transaction with the
// general description that the press opens (pageHistoryInput.ts), which makes a step only if
// something changed.
async function asUserAction(fn: () => void): Promise<void> {
    const transaction = history.beginTransaction(
        kGenericUndoDescriptionL10nKey,
    );
    fn();
    await letObserverDeliver();
    transaction.end();
}

// A click that selects element, as a click on it does.
function clickToSelect(element: HTMLElement): Promise<void> {
    return asUserAction(() =>
        theOneCanvasElementManager.setActiveElement(element),
    );
}

// Drag the selected element's se corner handle by (dx, dy), in a transaction of its own.
async function dragSeCorner(dx: number, dy: number): Promise<void> {
    const transaction = history.beginTransaction(kResizeDescription);
    const handle = document.querySelector(
        ".bloom-ui-canvas-element-resize-handle-se",
    );
    if (!handle) throw new Error("Test setup: no se resize handle");
    const startX = 100;
    const startY = 100;
    handle.dispatchEvent(
        new MouseEvent("mousedown", {
            bubbles: true,
            clientX: startX,
            clientY: startY,
            buttons: 1,
        }),
    );
    const move = new MouseEvent("mousemove", {
        bubbles: true,
        clientX: startX + dx,
        clientY: startY + dy,
        buttons: 1,
    });
    // jsdom's MouseEvent has no movementX or movementY.
    Object.defineProperties(move, {
        movementX: { value: dx },
        movementY: { value: dy },
    });
    document.dispatchEvent(move);
    document.dispatchEvent(
        new MouseEvent("mouseup", {
            bubbles: true,
            clientX: startX + dx,
            clientY: startY + dy,
        }),
    );
    await letObserverDeliver();
    transaction.end();
}

function geometry(element: HTMLElement) {
    return {
        left: element.style.left,
        top: element.style.top,
        width: element.style.width,
        height: element.style.height,
    };
}

describe("resizing a picture by a corner", () => {
    it("fits the box in the resize's step, so selecting the picture later makes no step and undo and redo put back its size exactly", async () => {
        const picture = document.getElementById("picture")!;
        const ce1 = document.getElementById("ce1")!;
        const before = geometry(picture);

        // Selecting a picture whose box fits it already writes nothing.
        await clickToSelect(picture);
        expect(geometry(picture)).toEqual(before);
        expect(history.undoDescriptionL10nKey).toBeUndefined();

        await dragSeCorner(40, 30);

        const resized = geometry(picture);
        // Sanity check: the drag changed the size.
        expect(resized.width).not.toBe(before.width);
        expect(history.undoDescriptionL10nKey).toBe(kResizeDescription);

        await clickToSelect(ce1);
        await clickToSelect(picture);

        // Selecting changed nothing and made no step.
        expect(geometry(picture)).toEqual(resized);
        expect(history.undoDescriptionL10nKey).toBe(kResizeDescription);

        await history.undo();

        expect(geometry(picture)).toEqual(before);
        // The resize was the only step.
        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe(kResizeDescription);

        await history.redo();

        expect(geometry(picture)).toEqual(resized);
        expect(history.undoDescriptionL10nKey).toBe(kResizeDescription);
        expect(history.redoDescriptionL10nKey).toBeUndefined();

        // And again, after the selection that the undo and the redo made.
        await clickToSelect(ce1);
        await clickToSelect(picture);
        await history.undo();
        expect(geometry(picture)).toEqual(before);
        expect(history.redoDescriptionL10nKey).toBe(kResizeDescription);
        await history.redo();
        expect(geometry(picture)).toEqual(resized);

        // The box the resize left fits the picture.
        const resizedRatio =
            parseFloat(resized.width) / parseFloat(resized.height);
        expect(resizedRatio).toBeCloseTo(kNaturalWidth / kNaturalHeight, 6);
    });
});

describe("the description of a drag of one of the control frame's handles", () => {
    it.each([
        [
            "picture",
            ".bloom-ui-canvas-element-resize-handle-se",
            "Resize(Image)",
        ],
        ["picture", ".bloom-ui-canvas-element-rotate-handle", "Rotate(Image)"],
        ["picture", ".bloom-ui-canvas-element-side-handle-e", "Image.Crop"],
        [
            "ce1",
            ".bloom-ui-canvas-element-side-handle-e",
            "Resize(SpeechBubble)",
        ],
        [
            "picture",
            ".bloom-ui-canvas-element-move-crop-handle",
            "Image.MoveInFrame",
        ],
    ])(
        "on %s, from %s, is %s",
        async (elementId, handleSelector, description) => {
            const element = document.getElementById(elementId)!;
            await clickToSelect(element);
            const handle = document.querySelector(handleSelector);
            if (!handle) throw new Error(`Test setup: no ${handleSelector}`);

            await asUserAction(() => {
                handle.dispatchEvent(
                    new MouseEvent("mousedown", { bubbles: true, buttons: 1 }),
                );
                // What the drag writes as the pointer moves; jsdom does no layout to drive it.
                element.style.top = "77px";
                document.dispatchEvent(
                    new MouseEvent("mouseup", { bubbles: true }),
                );
            });

            const undo = history.getStepDescriptions().undo;
            const latest = undo[undo.length - 1];
            expect(formatUndoDescription(latest.l10nId, latest.args)).toBe(
                description,
            );
        },
    );
});
