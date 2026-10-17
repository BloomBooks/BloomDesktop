// The Motion tool and the page history: the "Enable motion on this page" checkbox and the default
// rectangles that the tool saves when it opens are not recorded, so they make no undo step and
// join none. Dragging a rectangle is an undo step with its own description, and after an undo or
// redo the tool draws the rectangles again from the data-initialrect and data-finalrect that the
// undo put back. Removing the picture takes the rectangles' attributes away in the same step.
import {
    afterEach,
    beforeAll,
    beforeEach,
    describe,
    expect,
    it,
    vi,
} from "vitest";
import $ from "jquery";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";

// The stop handler, which jQuery UI calls at the end of a drag or a resize.
type StopHandler = { stop: (event?: Event, ui?: object) => void };

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
    // What the tool passed makeElement for each rectangle, by the rectangle's id.
    handlers: new Map<
        string,
        { resizable: StopHandler; draggable: StopHandler }
    >(),
}));
// The history that the code under test finds, with doOutsideHistory doing what the real
// one does when there is a history.
vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => mocks.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.doOutsideHistory(fn) : fn(),
    clearUndoAndRedo: () => mocks.history?.clearUndoAndRedo(),
}));
// The page frame's makeElement, without jQuery UI's draggable and resizable; the tests call the
// handlers that the tool passed for them.
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../js/workspaceFrames")>()),
    getEditablePageBundleExports: () => ({
        makeElement: (
            html: string,
            parent: JQuery,
            resizable: StopHandler,
            draggable: StopHandler,
        ) => {
            // Made in the page's document, as the page frame's makeElement does.
            const element = $(html, parent[0].ownerDocument);
            parent.append(element);
            mocks.handlers.set(element[0].id, { resizable, draggable });
            return element;
        },
    }),
}));
vi.mock("../canvas/canvasElementPageBridge", () => ({
    getCanvasElementManager: () => undefined,
}));

import { MotionTool } from "./motionTool";

const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kDragKey = "EditTab.Toolbox.Motion.Drag";
const kDeletePictureKey = "Test.DeletePicture";
const kStartRect = "0 0 0.5 0.5";
const kFinalRect = "0.25 0.25 0.5 0.5";

let history: PageHistory;
let onUnmarkedChange: ReturnType<typeof vi.fn>;
let tool: MotionTool;
let bloomCanvas: HTMLElement;

function pageDocument(): Document {
    return (document.getElementById("page") as HTMLIFrameElement)
        .contentDocument!;
}

function startRectangle(): HTMLElement | null {
    return pageDocument().getElementById("animationStart");
}

/** The tool's private members that these tests drive. */
function internals(): {
    motionChanged(checked: boolean): void;
    toggleMotionPreviewPlaying(): void;
} {
    return tool as unknown as {
        motionChanged(checked: boolean): void;
        toggleMotionPreviewPlaying(): void;
    };
}

/** The handlers that the tool gave jQuery UI for the rectangle that is drawn now with this id. */
function handlersOf(rectangle: HTMLElement): {
    resizable: StopHandler;
    draggable: StopHandler;
} {
    const handlers = mocks.handlers.get(rectangle.id);
    if (!handlers) throw new Error(`No handlers for #${rectangle.id}.`);
    return handlers;
}

/** Let the image observer's callback run. */
async function letObserversRun(): Promise<void> {
    await Promise.resolve();
}

/**
 * A page with these classes and one 400 by 300 bloom-canvas holding a picture, with these
 * attributes on the canvas, then moreHtml; and a history with one step that the user made before
 * the tool opened.
 */
async function setUpPage(
    canvasAttributes: string,
    pageClasses = "bloom-page",
    moreHtml = "",
): Promise<void> {
    pageDocument().body.innerHTML = `<div class="${pageClasses}"><div class="bloom-canvas" style="width: 400px; height: 300px" ${canvasAttributes}><div class="bloom-canvas-element bloom-backgroundImage"><div class="bloom-imageContainer"><img src="picture.png"></div></div></div><div id="other">before</div>${moreHtml}</div>`;
    bloomCanvas = pageDocument().querySelector(".bloom-canvas")!;
    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: pageDocument().querySelector(".bloom-page")!,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    mocks.history = history;
    await history.doTransaction(kEarlierKey, () => {
        pageDocument().getElementById("other")!.textContent = "typed";
    });
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
}

beforeAll(() => {
    const iframe = document.createElement("iframe");
    iframe.id = "page";
    document.body.appendChild(iframe);
    const doc = iframe.contentDocument!;
    if (!doc.body) {
        doc.write("<html><body></body></html>");
        doc.close();
    }
    // jsdom does no layout, so give elements the sizes and positions that their inline styles say.
    const pageElementPrototype = (
        iframe.contentWindow as unknown as { HTMLElement: typeof HTMLElement }
    ).HTMLElement.prototype;
    const fromStyle = (property: "width" | "height" | "top" | "left") =>
        function (this: HTMLElement) {
            return parseFloat(this.style[property]) || 0;
        };
    Object.defineProperty(pageElementPrototype, "offsetWidth", {
        get: fromStyle("width"),
        configurable: true,
    });
    Object.defineProperty(pageElementPrototype, "offsetHeight", {
        get: fromStyle("height"),
        configurable: true,
    });
    Object.defineProperty(pageElementPrototype, "offsetTop", {
        get: fromStyle("top"),
        configurable: true,
    });
    Object.defineProperty(pageElementPrototype, "offsetLeft", {
        get: fromStyle("left"),
        configurable: true,
    });
    // A preview scrolls the page to the top and back, which jsdom does not implement.
    iframe.contentWindow!.scrollTo = () => {};
    // The music player that the tool's control renders, which a preview pauses at its end.
    const musicPlayer = document.createElement("audio");
    musicPlayer.id = "pzMusicPlayer";
    musicPlayer.pause = () => {};
    document.body.appendChild(musicPlayer);
    if (!globalThis.ResizeObserver) {
        globalThis.ResizeObserver = class {
            public observe(): void {}
            public unobserve(): void {}
            public disconnect(): void {}
        };
    }
});

beforeEach(() => {
    // getComputedStyle gives no padding or border in jsdom; the elements here have none.
    vi.spyOn(
        MotionTool.prototype as unknown as {
            getWidth(el: HTMLElement): number;
        },
        "getWidth",
    ).mockImplementation((el) => el.offsetWidth);
    vi.spyOn(
        MotionTool.prototype as unknown as {
            getHeight(el: HTMLElement): number;
        },
        "getHeight",
    ).mockImplementation((el) => el.offsetHeight);
    tool = new MotionTool();
    // Stand-ins for the React control, which the tests do not render, and for the answer to
    // whether the subscription allows Motion.
    const state = { playing: false };
    (
        tool as unknown as {
            rootControl: {
                state: { playing: boolean };
                setState(change: object): void;
            };
        }
    ).rootControl = {
        state,
        setState: (change: object) => Object.assign(state, change),
    };
    (
        tool as unknown as { subscriptionAllowsMotion: boolean }
    ).subscriptionAllowsMotion = true;
});

afterEach(() => {
    tool.detachFromPage();
    history.dispose();
    mocks.history = undefined;
    mocks.handlers.clear();
    vi.restoreAllMocks();
});

describe("The Motion tool and the page history", () => {
    it("saves the default rectangles when it opens without making a step", async () => {
        await setUpPage("");
        expect(bloomCanvas.hasAttribute("data-initialrect")).toBe(false);

        // As when the tool opens from a click in the toolbox.
        const click = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        tool.newPageReady();
        expect(click.end()).toBe(false);

        expect(bloomCanvas.getAttribute("data-initialrect")).toBeTruthy();
        expect(bloomCanvas.getAttribute("data-finalrect")).toBeTruthy();
        expect(startRectangle()).not.toBeNull();
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("makes no step when the checkbox is unchecked or checked", async () => {
        await setUpPage(
            `data-initialrect="${kStartRect}" data-finalrect="${kFinalRect}"`,
        );
        tool.newPageReady();
        expect(startRectangle()).not.toBeNull();

        const uncheck = history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        internals().motionChanged(false);
        expect(uncheck.end()).toBe(false);
        expect(bloomCanvas.hasAttribute("data-initialrect")).toBe(false);
        expect(bloomCanvas.getAttribute("data-disabled-initialrect")).toBe(
            kStartRect,
        );
        expect(startRectangle()).toBeNull();

        const check = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        internals().motionChanged(true);
        expect(check.end()).toBe(false);
        expect(bloomCanvas.getAttribute("data-initialrect")).toBe(kStartRect);
        expect(startRectangle()).not.toBeNull();

        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
        await history.undo();
        expect(pageDocument().getElementById("other")!.textContent).toBe(
            "before",
        );
        expect(bloomCanvas.getAttribute("data-initialrect")).toBe(kStartRect);
    });

    it("draws the rectangles again after an undo or redo of a drag", async () => {
        await setUpPage(
            `data-initialrect="${kStartRect}" data-finalrect="${kFinalRect}"`,
        );
        tool.newPageReady();
        const drawnFirst = startRectangle()!;
        expect(drawnFirst.style.left).toBe("1px");

        // The user drags the rectangle further right. The press opens a transaction with the
        // general description (pageHistoryInput.ts), which closes after the release, where
        // jQuery UI calls the drag's stop handler.
        const press = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        drawnFirst.style.left = "101px";
        handlersOf(drawnFirst).draggable.stop();
        expect(press.end()).toBe(true);
        expect(history.undoDescriptionL10nKey).toBe(kDragKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
        const draggedRect = bloomCanvas.getAttribute("data-initialrect")!;
        expect(parseFloat(draggedRect.split(" ")[0])).toBeCloseTo(101 / 400);

        await history.undo();
        expect(bloomCanvas.getAttribute("data-initialrect")).toBe(kStartRect);
        const drawnAfterUndo = startRectangle()!;
        expect(drawnAfterUndo).not.toBe(drawnFirst);
        expect(drawnAfterUndo.style.left).toBe("1px");
        // Drawing the rectangles did not clear redo.
        expect(history.redoDescriptionL10nKey).toBe(kDragKey);

        await history.redo();
        expect(bloomCanvas.getAttribute("data-initialrect")).toBe(draggedRect);
        expect(parseFloat(startRectangle()!.style.left)).toBeCloseTo(102);

        // Once the tool has left the page, an undo no longer draws them.
        tool.detachFromPage();
        expect(startRectangle()).toBeNull();
        await history.undo();
        expect(startRectangle()).toBeNull();
    });

    it("makes resizing a rectangle one step with the drag's description", async () => {
        await setUpPage(
            `data-initialrect="${kStartRect}" data-finalrect="${kFinalRect}"`,
        );
        tool.newPageReady();
        const finalRectangle = pageDocument().getElementById("animationEnd")!;

        // With no transaction open around it, the resize's stop handler makes its own step.
        finalRectangle.style.width = "100px";
        handlersOf(finalRectangle).resizable.stop();

        expect(history.hasOpenTransaction).toBe(false);
        expect(history.undoDescriptionL10nKey).toBe(kDragKey);
        expect(bloomCanvas.getAttribute("data-finalrect")).not.toBe(kFinalRect);
        await history.undo();
        expect(bloomCanvas.getAttribute("data-finalrect")).toBe(kFinalRect);
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });

    it("removing the picture takes the rectangles away in the same step, and undo puts both back", async () => {
        await setUpPage(
            `data-initialrect="${kStartRect}" data-finalrect="${kFinalRect}"`,
        );
        tool.newPageReady();
        expect(startRectangle()).not.toBeNull();
        const img = bloomCanvas.querySelector("img")!;

        await history.doTransaction(kDeletePictureKey, () =>
            img.setAttribute("src", "placeHolder.png"),
        );
        await letObserversRun();

        expect(bloomCanvas.hasAttribute("data-initialrect")).toBe(false);
        expect(bloomCanvas.hasAttribute("data-finalrect")).toBe(false);
        expect(startRectangle()).toBeNull();
        expect(onUnmarkedChange).not.toHaveBeenCalled();
        expect(history.undoDescriptionL10nKey).toBe(kDeletePictureKey);

        await history.undo();
        await letObserversRun();
        expect(img.getAttribute("src")).toBe("picture.png");
        expect(bloomCanvas.getAttribute("data-initialrect")).toBe(kStartRect);
        expect(bloomCanvas.getAttribute("data-finalrect")).toBe(kFinalRect);
        expect(startRectangle()).not.toBeNull();
        // What the tool did after the undo did not clear redo.
        expect(history.redoDescriptionL10nKey).toBe(kDeletePictureKey);

        await history.redo();
        await letObserversRun();
        expect(img.getAttribute("src")).toBe("placeHolder.png");
        expect(bloomCanvas.hasAttribute("data-initialrect")).toBe(false);
        expect(startRectangle()).toBeNull();
        expect(history.undoDescriptionL10nKey).toBe(kDeletePictureKey);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("a preview makes no step, joins none, and leaves the page's classes as they were", async () => {
        await setUpPage(
            `data-initialrect="${kStartRect}" data-finalrect="${kFinalRect}"`,
            "bloom-page A5Landscape Landscape",
            '<div id="said" class="audio-sentence ui-audioCurrent">Said.</div>',
        );
        tool.newPageReady();
        const bloomPage = pageDocument().querySelector(".bloom-page")!;
        const pageClassesBefore = bloomPage.getAttribute("class");

        internals().toggleMotionPreviewPlaying();
        expect(
            pageDocument().body.classList.contains(
                "bloom-hidden-for-animation",
            ),
        ).toBe(true);
        internals().toggleMotionPreviewPlaying();

        expect(
            pageDocument().body.classList.contains(
                "bloom-hidden-for-animation",
            ),
        ).toBe(false);
        expect(bloomPage.getAttribute("class")).toBe(pageClassesBefore);
        expect(
            pageDocument()
                .getElementById("said")!
                .classList.contains("ui-audioCurrent"),
        ).toBe(false);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    });
});
