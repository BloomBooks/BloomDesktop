// Turning canvas editing off (opening a tool, or saving the page) saves each canvas element's place
// as the alternate for the current language. That is worked out from the element, so it is not
// recorded in the page history, and it is written only when it changes; a resize of the
// bloom-canvas writes it the same way. Turning editing on again applies an element's alternate
// only the first time after the page loads, and never to an element added since, so that the
// alternate written when editing was turned off does not overwrite what an undo put back meanwhile.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";

// comicaljs draws bubbles on a canvas, which jsdom lacks; these functions do not draw.
vi.mock("comicaljs", () => ({
    Bubble: class {
        static getBubbleSpec() {
            return { spec: "none", tails: [] };
        }
    },
}));
const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
// The history that the code under test finds, with doOutsideHistory doing what the real
// one does when there is a history.
vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => mocks.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.doOutsideHistory(fn) : fn(),
    addToLatestTransaction: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.addToLatestTransaction(fn) : fn(),
}));

import {
    adjustCanvasElementAlternates,
    adjustCanvasElementsForCurrentLanguage,
    saveCurrentCanvasElementStateAsCurrentLangAlternate,
} from "./CanvasElementAlternates";

const kAddKey = "EditTab.Toolbox.ComicTool.Options.AddSpeechBubble";
const kMoveKey = "EditTab.Toolbox.ComicTool.Options.Move";
const kResizeKey = "TestResizeOfTheBloomCanvas";
const kStyleOnAdding = "left: 10px; top: 20px; width: 100px; height: 50px;";
const kStyleAfterMoving = "left: 60px; top: 20px; width: 100px; height: 50px;";
const kStyleAfterResizing =
    "left: 20px; top: 40px; width: 100px; height: 50px;";

let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn>;
let page: HTMLElement;
let bloomCanvas: HTMLElement;

beforeEach(() => {
    (globalThis as unknown as { GetSettings: () => object }).GetSettings =
        () => ({ languageForNewTextBoxes: "en" });
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-canvas"><img src="a.png"></div><div id="other"></div>';
    document.body.appendChild(page);
    bloomCanvas = page.querySelector(".bloom-canvas")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    mocks.history = history;
});

afterEach(() => {
    history.dispose();
    mocks.history = undefined;
    page.remove();
});

/** Add a speech bubble as one undo step, as the Canvas tool does, and return it. */
async function addBubble(): Promise<HTMLElement> {
    let bubble: HTMLElement | undefined;
    await history.doTransaction(kAddKey, () => {
        bubble = document.createElement("div");
        bubble.className = "bloom-canvas-element";
        bubble.setAttribute("style", kStyleOnAdding);
        bubble.setAttribute("data-bubble", "{`version`:`1.0`,`tails`:[]}");
        bubble.innerHTML =
            '<div class="bloom-translationGroup"><div class="bloom-editable" lang="en"><p>Hi</p></div></div>';
        bloomCanvas.appendChild(bubble);
    });
    expect(history.undoDescriptionL10nKey).toBe(kAddKey);
    return bubble!;
}

function alternateOf(bubble: HTMLElement): string | null {
    return bubble
        .querySelector(".bloom-editable")!
        .getAttribute("data-bubble-alternate");
}

describe("The current language's alternate and the page history", () => {
    it("saving it keeps the step that added the bubble undoable", async () => {
        const bubble = await addBubble();
        expect(alternateOf(bubble)).toBeNull();

        saveCurrentCanvasElementStateAsCurrentLangAlternate(bloomCanvas);

        expect(alternateOf(bubble)).toContain(kStyleOnAdding);
        expect(history.undoDescriptionL10nKey).toBe(kAddKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();

        await history.undo();
        expect(bubble.isConnected).toBe(false);
        expect(history.redoDescriptionL10nKey).toBe(kAddKey);
        await history.redo();
        expect(bubble.parentElement).toBe(bloomCanvas);
        expect(alternateOf(bubble)).toContain(kStyleOnAdding);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("saving it after an undo keeps redo", async () => {
        const bubble = await addBubble();
        await history.doTransaction(kMoveKey, () =>
            bubble.setAttribute("style", kStyleAfterMoving),
        );
        await history.undo();
        expect(history.redoDescriptionL10nKey).toBe(kMoveKey);

        saveCurrentCanvasElementStateAsCurrentLangAlternate(bloomCanvas);

        expect(alternateOf(bubble)).toContain(kStyleOnAdding);
        expect(history.redoDescriptionL10nKey).toBe(kMoveKey);
        await history.redo();
        expect(bubble.getAttribute("style")).toBe(kStyleAfterMoving);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("saving it again when nothing has changed writes nothing", async () => {
        const bubble = await addBubble();
        saveCurrentCanvasElementStateAsCurrentLangAlternate(bloomCanvas);
        const editable = bubble.querySelector(".bloom-editable")!;
        const setAttribute = vi.spyOn(editable, "setAttribute");

        saveCurrentCanvasElementStateAsCurrentLangAlternate(bloomCanvas);

        expect(setAttribute).not.toHaveBeenCalled();
    });

    it("turning editing on again leaves the place that an undo put back while it was off", async () => {
        const bubble = await addBubble();
        // The page loads, and canvas editing is turned on for the first time.
        adjustCanvasElementsForCurrentLanguage(bloomCanvas);
        await history.doTransaction(kMoveKey, () =>
            bubble.setAttribute("style", kStyleAfterMoving),
        );
        // A tool opens, turning canvas editing off, and the user undoes the move.
        saveCurrentCanvasElementStateAsCurrentLangAlternate(bloomCanvas);
        expect(alternateOf(bubble)).toContain(kStyleAfterMoving);
        await history.undo();
        expect(bubble.getAttribute("style")).toBe(kStyleOnAdding);

        // The tool closes, turning canvas editing on.
        adjustCanvasElementsForCurrentLanguage(bloomCanvas);

        expect(bubble.getAttribute("style")).toBe(kStyleOnAdding);
        expect(history.redoDescriptionL10nKey).toBe(kMoveKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("turning editing on again leaves the place that an undo put back for a bubble added since it was first on", async () => {
        // The page loads with no bubble, and canvas editing is turned on for the first time.
        adjustCanvasElementsForCurrentLanguage(bloomCanvas);
        const bubble = await addBubble();
        await history.doTransaction(kMoveKey, () =>
            bubble.setAttribute("style", kStyleAfterMoving),
        );
        // A tool such as Image Description opens, turning canvas editing off, and the user
        // undoes the move.
        saveCurrentCanvasElementStateAsCurrentLangAlternate(bloomCanvas);
        expect(alternateOf(bubble)).toContain(kStyleAfterMoving);
        await history.undo();
        expect(bubble.getAttribute("style")).toBe(kStyleOnAdding);

        // The tool closes, turning canvas editing on.
        adjustCanvasElementsForCurrentLanguage(bloomCanvas);

        expect(bubble.getAttribute("style")).toBe(kStyleOnAdding);
        expect(history.redoDescriptionL10nKey).toBe(kMoveKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("a resize of the bloom-canvas stays undoable after editing is turned off", async () => {
        const bubble = await addBubble();
        adjustCanvasElementsForCurrentLanguage(bloomCanvas);
        const french = document.createElement("div");
        french.className = "bloom-editable";
        french.setAttribute("lang", "fr");
        french.setAttribute(
            "data-bubble-alternate",
            `{\`lang\`:\`fr\`,\`style\`:\`${kStyleOnAdding}\`,\`tails\`:[]}`,
        );
        history.doOutsideHistory(() =>
            bubble
                .querySelector(".bloom-translationGroup")!
                .appendChild(french),
        );
        // A splitter drag resizes the bloom-canvas, which moves the bubble and its alternates
        // (adjustCanvasElementChildrenIfSizeChanged).
        await history.doTransaction(kResizeKey, () => {
            bubble.setAttribute("style", kStyleAfterResizing);
            adjustCanvasElementAlternates(bubble, 1, 0, 0, 10, 20);
        });
        expect(alternateOf(bubble)).toContain(kStyleAfterResizing);
        const frenchAlternateAfterResizing = french.getAttribute(
            "data-bubble-alternate",
        );
        expect(frenchAlternateAfterResizing).toContain("left: 20px");

        // A tool opens, turning canvas editing off.
        saveCurrentCanvasElementStateAsCurrentLangAlternate(bloomCanvas);
        await history.undo();

        expect(bubble.getAttribute("style")).toBe(kStyleOnAdding);
        expect(french.getAttribute("data-bubble-alternate")).toContain(
            kStyleOnAdding,
        );
        expect(history.redoDescriptionL10nKey).toBe(kResizeKey);
        expect(history.undoDescriptionL10nKey).toBe(kAddKey);
        await history.redo();
        expect(bubble.getAttribute("style")).toBe(kStyleAfterResizing);
        expect(french.getAttribute("data-bubble-alternate")).toBe(
            frenchAlternateAfterResizing,
        );
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("turning editing on for the first time puts the element where its alternate says", () => {
        const bubble = document.createElement("div");
        bubble.className = "bloom-canvas-element";
        bubble.setAttribute("style", kStyleOnAdding);
        bubble.setAttribute("data-bubble", "{`version`:`1.0`,`tails`:[]}");
        bubble.innerHTML = `<div class="bloom-translationGroup"><div class="bloom-editable" lang="en" data-bubble-alternate="{\`lang\`:\`en\`,\`style\`:\`${kStyleAfterMoving}\`,\`tails\`:[]}"><p>Hi</p></div></div>`;
        history.doOutsideHistory(() => bloomCanvas.appendChild(bubble));

        adjustCanvasElementsForCurrentLanguage(bloomCanvas);

        expect(bubble.getAttribute("style")).toBe(kStyleAfterMoving);
    });
});
