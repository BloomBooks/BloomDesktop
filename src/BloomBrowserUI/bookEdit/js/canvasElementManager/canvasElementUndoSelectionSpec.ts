// Tests of which canvas element an undo or redo selects (CanvasElementManager.updateAfterUndoOrRedo),
// with a real page history and the real edit-time markup filter, wired to the manager by
// listenToPageHistory as in the Edit tab. Each test also checks that selecting leaves redo
// available and working: what selecting writes to the page must not join the latest step. Also
// tests that selecting after an undo or redo leaves a box that does not fit its picture or video as
// the undo or redo left it, and that a text canvas element that an undo puts back gets its source
// bubbles again.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Selecting an element
// only asks Comical to activate it, draw the bubbles again, find the element's family, and read
// its bubble style and tails; these canvas elements have none.
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
    CanvasElementManager,
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "./CanvasElementManager";
import {
    listenToPageHistory,
    whenNoRequestPageContentDelays,
} from "../bloomEditing";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";
import BloomSourceBubbles from "../../sourceBubbles/BloomSourceBubbles";
import $ from "jquery";

const kMoveDescription = "EditTab.Undo.Move";

let page: HTMLElement;
let history: PageHistory;

// A page with a text box outside the bloom-canvas, and two canvas elements in it.
beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-translationGroup">
                <div class="bloom-editable bloom-visibility-code-on" lang="en"><p id="para">Hello</p></div>
            </div>
            <div class="bloom-canvas" id="canvas" data-title="A picture">
                <div class="bloom-canvas-element" id="ce1" style="left: 10px; top: 10px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" lang="en"><p>One</p></div>
                    </div>
                </div>
                <div class="bloom-canvas-element" id="ce2" style="left: 100px; top: 10px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" lang="en"><p>Two</p></div>
                    </div>
                </div>
                <div class="bloom-canvas-element" id="videoElement" style="left: 200px; top: 100px; width: 80px; height: 60px;">
                    <div class="bloom-videoContainer"><video></video></div>
                </div>
                <div class="bloom-canvas-element bloom-backgroundImage" id="background" style="left: 0px; top: 0px; width: 300px; height: 200px;">
                    <div class="bloom-imageContainer"><img id="backgroundImg" src="a.png" alt=""></div>
                </div>
            </div>
        </div>`;
    page = document.getElementById("page")!;
    initializeCanvasElementManager();
    theOneCanvasElementManager.isCanvasElementEditingOn = true;
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
    });
    pageFrame.history = history;
    listenToPageHistory(history);
    select(undefined);
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
    vi.restoreAllMocks();
});

function byId(id: string): HTMLElement {
    const element = document.getElementById(id);
    if (!element) throw new Error(`Test setup: no element with id ${id}`);
    return element;
}

function activeElement(): HTMLElement | undefined {
    return theOneCanvasElementManager.getActiveElement();
}

// Select an element as a click does. A click is not an undo step, so it is made outside the
// history, as the stand-in transaction that Bloom opens on a click makes no step for it.
function select(element: HTMLElement | undefined): void {
    history.doOutsideHistory(() =>
        theOneCanvasElementManager.setActiveElement(element),
    );
}

function activeMarkedElements(): Element[] {
    return Array.from(document.querySelectorAll("[data-bloom-active]"));
}

describe("selecting the canvas element that an undo or redo changed", () => {
    it("undoing a move selects the moved element, and redo still works", async () => {
        const ce1 = byId("ce1");
        const ce2 = byId("ce2");
        select(ce1);
        await history.doTransaction(kMoveDescription, () => {
            ce1.style.left = "40px";
        });
        select(ce2);
        expect(activeElement()).toBe(ce2);
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);

        await history.undo();

        expect(ce1.style.left).toBe("10px");
        expect(activeElement()).toBe(ce1);
        expect(activeMarkedElements()).toEqual([ce1]);
        expect(history.redoDescriptionL10nKey).toBe(kMoveDescription);

        select(ce2);
        await history.redo();

        expect(ce1.style.left).toBe("40px");
        expect(activeElement()).toBe(ce1);
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("undoing a delete selects the restored element, and redo deletes it again", async () => {
        const ce1 = byId("ce1");
        const ce2 = byId("ce2");
        select(ce1);
        await history.doTransaction("EditTab.Undo.Delete", () => {
            ce1.remove();
            theOneCanvasElementManager.setActiveElement(ce2);
        });
        expect(ce1.isConnected).toBe(false);
        expect(activeElement()).toBe(ce2);

        await history.undo();

        expect(ce1.isConnected).toBe(true);
        expect(activeElement()).toBe(ce1);
        expect(activeMarkedElements()).toEqual([ce1]);
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Delete");

        await history.redo();

        expect(ce1.isConnected).toBe(false);
        // The redo took the active element out of the page and put none back.
        expect(activeElement()).toBeUndefined();
        expect(activeMarkedElements()).toEqual([]);
        expect(history.undoDescriptionL10nKey).toBe("EditTab.Undo.Delete");
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("undoing an add leaves nothing selected, and redo selects the added element", async () => {
        const canvas = byId("canvas");
        const added = document.createElement("div");
        added.className = "bloom-canvas-element";
        added.id = "ce3";
        added.setAttribute(
            "style",
            "left: 200px; top: 10px; width: 50px; height: 30px;",
        );
        await history.doTransaction("EditTab.Undo.Add", () => {
            canvas.appendChild(added);
            theOneCanvasElementManager.setActiveElement(added);
        });
        expect(activeElement()).toBe(added);

        await history.undo();

        expect(added.isConnected).toBe(false);
        expect(activeElement()).toBeUndefined();
        expect(activeMarkedElements()).toEqual([]);
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Add");

        await history.redo();

        expect(added.isConnected).toBe(true);
        expect(activeElement()).toBe(added);
        expect(history.undoDescriptionL10nKey).toBe("EditTab.Undo.Add");
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    // Restoring the selection can put the caret in the text of a canvas element that the step
    // did not change, and the browser then focuses that text, whose focus handler makes that
    // element active. jsdom does not focus on setting the selection, so the test does it as the
    // browser would.
    it("undoing an add selects nothing, even when the restored caret is in another canvas element", async () => {
        const ce2 = byId("ce2");
        const ce2Text = ce2.querySelector<HTMLElement>(".bloom-editable")!;
        history.doOutsideHistory(() =>
            ce2Text.setAttribute("contenteditable", "true"),
        );
        ce2Text.addEventListener(
            "focusin",
            CanvasElementManager.onFocusSetActiveElement,
        );
        const selection = document.getSelection()!;
        selection.collapse(ce2Text.querySelector("p")!.firstChild, 1);
        const added = document.createElement("div");
        added.className = "bloom-canvas-element";
        added.setAttribute(
            "style",
            "left: 200px; top: 10px; width: 50px; height: 30px;",
        );
        await history.doTransaction("EditTab.Undo.Add", () => {
            byId("canvas").appendChild(added);
            theOneCanvasElementManager.setActiveElement(added);
        });
        selection.removeAllRanges();
        const setBaseAndExtent = selection.setBaseAndExtent.bind(selection);
        let activeAfterRestoringCaret: HTMLElement | undefined;
        const restoring = vi
            .spyOn(selection, "setBaseAndExtent")
            .mockImplementation((...args) => {
                setBaseAndExtent(...args);
                (args[0] as Node).parentElement
                    ?.closest<HTMLElement>(".bloom-editable")
                    ?.focus();
                activeAfterRestoringCaret = activeElement();
            });

        await history.undo();

        // Sanity check: the restored caret made ce2 active before the canvas element manager
        // brought the selection in line with the undo.
        expect(restoring).toHaveBeenCalledTimes(1);
        expect(activeAfterRestoringCaret).toBe(ce2);
        expect(added.isConnected).toBe(false);
        expect(activeElement()).toBeUndefined();
        expect(activeMarkedElements()).toEqual([]);
        expect(ce2.classList.contains("bloom-focusedCanvasElement")).toBe(
            false,
        );
        expect(ce2.contains(document.activeElement)).toBe(false);
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Add");
    });

    it("undoing typing outside any canvas element keeps the selection", async () => {
        const ce2 = byId("ce2");
        const paragraph = byId("para");
        select(ce2);
        await history.doTransaction("EditTab.Undo.Typing", () => {
            paragraph.textContent = "Hello there";
        });

        await history.undo();

        expect(paragraph.textContent).toBe("Hello");
        expect(activeElement()).toBe(ce2);
        expect(activeMarkedElements()).toEqual([ce2]);
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Typing");

        await history.redo();

        expect(paragraph.textContent).toBe("Hello there");
        expect(activeElement()).toBe(ce2);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("undoing a change to the text of an unselected canvas element selects it", async () => {
        const ce1 = byId("ce1");
        const ce2 = byId("ce2");
        const text = ce1.querySelector("p")!;
        await history.doTransaction("EditTab.Undo.Typing", () => {
            text.textContent = "One more";
        });
        select(ce2);

        await history.undo();

        expect(text.textContent).toBe("One");
        expect(activeElement()).toBe(ce1);
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Typing");
    });

    // Selecting a canvas element that holds a video gives its video container the class
    // bloom-selected; this checks that selecting it after an undo does not clear redo. jsdom
    // loads no video, so the video's size is unknown while the undo selects it; a click would fit
    // the box to the video once its metadata loads, but the undo must not.
    it("undoing a move of a video selects it, and redo still works", async () => {
        const videoElement = byId("videoElement");
        const video = videoElement.querySelector("video")!;
        const videoContainer = videoElement.querySelector(
            ".bloom-videoContainer",
        )!;
        await history.doTransaction(kMoveDescription, () => {
            videoElement.style.left = "220px";
        });
        select(byId("ce1"));
        // Sanity check: selecting another canvas element leaves the video container unselected.
        expect(videoContainer.classList.contains("bloom-selected")).toBe(false);

        await history.undo();

        expect(videoElement.style.left).toBe("200px");
        expect(activeElement()).toBe(videoElement);
        expect(videoContainer.classList.contains("bloom-selected")).toBe(true);
        expect(history.redoDescriptionL10nKey).toBe(kMoveDescription);
        // The 80 by 60 box does not fit a 16:9 video.
        Object.defineProperty(video, "videoWidth", { get: () => 160 });
        Object.defineProperty(video, "videoHeight", { get: () => 90 });
        video.dispatchEvent(new Event("loadedmetadata"));
        expect(videoElement.style.height).toBe("60px");
        expect(history.redoDescriptionL10nKey).toBe(kMoveDescription);

        await history.redo();

        expect(videoElement.style.left).toBe("220px");
        expect(activeElement()).toBe(videoElement);
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);
    });

    // Selecting the background image puts the bloom-canvas's data-title into its title.
    it("undoing a change of image selects the background image, and redo still works", async () => {
        const background = byId("background");
        const image = byId("backgroundImg") as HTMLImageElement;
        const canvas = byId("canvas");
        await history.doTransaction("EditTab.Undo.ChangeImage", () => {
            image.setAttribute("src", "b.png");
        });
        select(byId("ce1"));
        // Sanity check: with a canvas element other than the background image selected, the
        // bloom-canvas shows no tooltip.
        expect(canvas.getAttribute("title")).toBe("");

        await history.undo();

        expect(image.getAttribute("src")).toBe("a.png");
        expect(activeElement()).toBe(background);
        expect(canvas.getAttribute("title")).toBe("A picture");
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.ChangeImage");

        await history.redo();

        expect(image.getAttribute("src")).toBe("b.png");
        expect(activeElement()).toBe(background);
        expect(history.undoDescriptionL10nKey).toBe("EditTab.Undo.ChangeImage");
    });

    it("keeps the active element when it is one of several that the undo changed", async () => {
        const ce1 = byId("ce1");
        const ce2 = byId("ce2");
        await history.doTransaction(kMoveDescription, () => {
            ce1.style.top = "20px";
            ce2.style.top = "20px";
        });
        select(ce2);

        await history.undo();

        expect(ce1.style.top).toBe("10px");
        expect(ce2.style.top).toBe("10px");
        expect(activeElement()).toBe(ce2);

        select(undefined);
        await history.redo();

        // With none of them active, the first in document order.
        expect(activeElement()).toBe(ce1);
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);
    });
});

// A click on a canvas element whose box does not have its picture's shape fits the box to the
// picture (adjustContainerAspectRatio). Selecting the element after an undo or redo must not: the
// fit would write a size and position that the next redo does not expect.
describe("selecting after an undo or redo leaves a box that does not fit its picture", () => {
    let pictureElement: HTMLElement;
    let picture: HTMLImageElement;
    // The picture's natural size, which jsdom does not load; zero until it has loaded.
    let pictureSize: { width: number; height: number };

    // A square box holding a picture.
    beforeEach(() => {
        pictureElement = document.createElement("div");
        pictureElement.className = "bloom-canvas-element";
        pictureElement.id = "pictureElement";
        pictureElement.setAttribute(
            "style",
            "left: 10px; top: 100px; width: 60px; height: 60px;",
        );
        pictureElement.innerHTML =
            '<div class="bloom-imageContainer"><img src="c.png" alt=""></div>';
        picture = pictureElement.querySelector("img")!;
        Object.defineProperty(picture, "naturalWidth", {
            get: () => pictureSize.width,
        });
        Object.defineProperty(picture, "naturalHeight", {
            get: () => pictureSize.height,
        });
        history.doOutsideHistory(() =>
            byId("canvas").appendChild(pictureElement),
        );
    });

    /** The box's position and size, as its style gives them. */
    function box(): string {
        const style = pictureElement.style;
        return `${style.left} ${style.top} ${style.width} ${style.height}`;
    }

    it("when the undo and the redo select the element", async () => {
        // A picture twice as wide as it is high, which the square box does not fit.
        pictureSize = { width: 120, height: 60 };
        await history.doTransaction(kMoveDescription, () => {
            pictureElement.style.left = "40px";
        });
        select(byId("ce1"));
        expect(box()).toBe("40px 100px 60px 60px");

        await history.undo();

        expect(activeElement()).toBe(pictureElement);
        expect(box()).toBe("10px 100px 60px 60px");
        expect(history.redoDescriptionL10nKey).toBe(kMoveDescription);

        select(byId("ce1"));
        await history.redo();

        expect(activeElement()).toBe(pictureElement);
        expect(box()).toBe("40px 100px 60px 60px");
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);
        expect(history.redoDescriptionL10nKey).toBeUndefined();

        // A click on the element still fits it.
        select(pictureElement);
        expect(pictureElement.style.width).toBe("60px");
        expect(pictureElement.style.height).toBe("30px");
    });

    it("when the element the undo changed is already selected", async () => {
        // A square picture, which the box fits when the element is selected.
        pictureSize = { width: 60, height: 60 };
        select(pictureElement);
        expect(box()).toBe("10px 100px 60px 60px");
        await history.doTransaction(kMoveDescription, () => {
            pictureElement.style.left = "40px";
        });
        // The picture's file is now one twice as wide as it is high.
        pictureSize = { width: 120, height: 60 };

        await history.undo();

        expect(activeElement()).toBe(pictureElement);
        expect(box()).toBe("10px 100px 60px 60px");
        expect(history.redoDescriptionL10nKey).toBe(kMoveDescription);

        await history.redo();

        expect(box()).toBe("40px 100px 60px 60px");
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });

    it("when a click's fit is still waiting for the picture as an undo begins", async () => {
        pictureSize = { width: 0, height: 0 };
        // The click's fit waits for the picture to load.
        select(pictureElement);
        let saveGoesAhead = false;
        void whenNoRequestPageContentDelays().then(
            () => (saveGoesAhead = true),
        );
        await Promise.resolve();
        expect(saveGoesAhead).toBe(false);
        await history.doTransaction(kMoveDescription, () => {
            byId("ce1").style.left = "40px";
        });

        await history.undo();

        await Promise.resolve();
        await Promise.resolve();
        expect(saveGoesAhead).toBe(true);
        pictureSize = { width: 120, height: 60 };
        picture.dispatchEvent(new Event("load"));
        expect(box()).toBe("10px 100px 60px 60px");
        expect(history.redoDescriptionL10nKey).toBe(kMoveDescription);

        // A click on the element still fits it.
        select(pictureElement);
        expect(pictureElement.style.height).toBe("30px");
    });

    it("when the picture loads after the undo", async () => {
        pictureSize = { width: 0, height: 0 };
        await history.doTransaction(kMoveDescription, () => {
            pictureElement.style.left = "40px";
        });
        select(byId("ce1"));

        await history.undo();

        expect(activeElement()).toBe(pictureElement);
        pictureSize = { width: 120, height: 60 };
        picture.dispatchEvent(new Event("load"));
        expect(box()).toBe("10px 100px 60px 60px");
        expect(history.redoDescriptionL10nKey).toBe(kMoveDescription);

        await history.redo();

        expect(box()).toBe("40px 100px 60px 60px");
        expect(history.undoDescriptionL10nKey).toBe(kMoveDescription);
    });
});

describe("a text canvas element that an undo or redo puts back", () => {
    it("gets its source bubbles again when the undo of its delete puts it back", async () => {
        // jsdom has no qtip; record what would become the bubbles instead.
        const sourceBubbleContents = $("<div>source text</div>");
        vi.spyOn(BloomSourceBubbles, "ProduceSourceBubbles").mockReturnValue(
            sourceBubbleContents,
        );
        const makeQtips = vi
            .spyOn(BloomSourceBubbles, "MakeSourceBubblesIntoQtips")
            .mockImplementation(() => {});
        const ce1 = byId("ce1");
        const ce2 = byId("ce2");
        const translationGroup = ce1.querySelector(".bloom-translationGroup");
        select(ce1);
        await history.doTransaction("EditTab.Undo.Delete", () => {
            ce1.remove();
            theOneCanvasElementManager.setActiveElement(ce2);
        });
        // Sanity check: the delete itself made no bubbles.
        expect(makeQtips).not.toHaveBeenCalled();

        await history.undo();

        expect(ce1.isConnected).toBe(true);
        expect(makeQtips.mock.calls).toEqual([
            [translationGroup, sourceBubbleContents],
        ]);
        expect(history.redoDescriptionL10nKey).toBe("EditTab.Undo.Delete");

        // A redo that takes it out again, and an undo that puts back no canvas element, make none.
        makeQtips.mockClear();
        await history.redo();
        expect(ce1.isConnected).toBe(false);
        await history.doTransaction(kMoveDescription, () => {
            ce2.style.left = "120px";
        });
        await history.undo();
        expect(ce2.style.left).toBe("100px");
        expect(makeQtips).not.toHaveBeenCalled();
    });
});
