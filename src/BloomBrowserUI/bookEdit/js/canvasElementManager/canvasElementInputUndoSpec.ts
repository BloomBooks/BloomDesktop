// Tests that the undo step of a press, a drag or a key press on a canvas element in the page has
// a description that says what the user did. Uses a real page history, and stands in for the
// transaction with the general description that the press or key press opens
// (pageHistoryInput.ts). The pointer and keyboard code runs with a stand-in for the canvas
// element manager.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// What the press finds under the pointer: a canvas element, or a tail handle (Comical's
// draggable), set by each test.
const hits = vi.hoisted(() => ({
    bubbleContent: undefined as HTMLElement | undefined,
    tailHandle: false,
    tableBoundary: false,
}));

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us.
vi.mock("comicaljs", () => {
    class Bubble {
        public constructor(public content: HTMLElement) {}
        public static getBubbleSpec() {
            return { style: "none", tails: [] };
        }
    }
    return {
        Bubble,
        Comical: {
            getBubbleHit: () =>
                hits.bubbleContent ? new Bubble(hits.bubbleContent) : undefined,
            isDraggableNear: () => hits.tailHandle,
            findRelatives: () => [],
        },
    };
});
vi.mock("bloom-table", () => ({
    dragToResize: {
        beginResizeAtPoint: () => hits.tableBoundary,
        resizeEdgeAtPoint: () => undefined,
    },
}));
vi.mock("../tableFeature", () => ({
    tablesMayBeRestructured: () => true,
}));
// The right-click menu and the video controls, which these tests never reach.
vi.mock("./CanvasElementContextControls", () => ({
    renderCanvasElementContextControls: () => {},
}));
vi.mock("../bloomVideo", () => ({ handlePlayClick: () => {} }));

// Code in the page frame finds the page history through the page frame's bundle exports; give it
// this test's.
const pageFrame = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("../workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../workspaceFrames")>()),
    getEditablePageBundleExports: () => ({
        pageHistory: () => pageFrame.history,
    }),
}));

// These imports deliberately come after the vi.mock calls above, so that the module graph they
// pull in gets the stubbed modules.
import {
    CanvasElementPointerInteractions,
    type ICanvasElementPointerInteractionsHost,
} from "./CanvasElementPointerInteractions";
import { CanvasElementKeyboardProvider } from "./CanvasElementKeyboardProvider";
import { CanvasSnapProvider } from "./CanvasSnapProvider";
import { CanvasGuideProvider } from "./CanvasGuideProvider";
import { createPageHistory } from "../../pageHistory/pageHistory";
import {
    formatUndoDescription,
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";

let page: HTMLElement;
let canvas: HTMLElement;
let ce1: HTMLElement;
let history: PageHistory;

beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page" id="page">
            <div class="bloom-canvas bloom-has-canvas-element" id="canvas">
                <canvas id="comical"></canvas>
                <div class="bloom-canvas-element" id="ce1" style="left: 10px; top: 10px; width: 50px; height: 30px;">
                    <div class="bloom-translationGroup">
                        <div class="bloom-editable bloom-visibility-code-on" lang="en"><p>One</p></div>
                    </div>
                </div>
            </div>
        </div>`;
    page = document.getElementById("page")!;
    canvas = document.getElementById("canvas")!;
    ce1 = document.getElementById("ce1")!;
    hits.bubbleContent = undefined;
    hits.tailHandle = false;
    hits.tableBoundary = false;
    history = createPageHistory({ root: page });
    pageFrame.history = history;
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    document.body.innerHTML = "";
});

// The description of the newest undo step, as the log writes it.
function latestStep(): string {
    const undo = history.getStepDescriptions().undo;
    expect(undo.length, "there should be an undo step").toBeGreaterThan(0);
    const latest = undo[undo.length - 1];
    return formatUndoDescription(latest.l10nId, latest.args);
}

// Run fn as Bloom runs the handlers of a press or a key press: inside the transaction with the
// general description that it opens (pageHistoryInput.ts), which makes a step only if something
// changed.
async function asUserAction(fn: () => void): Promise<void> {
    const transaction = history.beginTransaction(
        kGenericUndoDescriptionL10nKey,
    );
    fn();
    // A MutationObserver delivers its records in a microtask.
    await Promise.resolve();
    transaction.end();
}

describe("the description of a press or a drag on a canvas element", () => {
    let interactions: CanvasElementPointerInteractions;
    let activeElement: HTMLElement | undefined;

    beforeEach(() => {
        activeElement = undefined;
        const host = {
            getActiveElement: () => activeElement,
            setActiveElement: (element: HTMLElement | undefined) =>
                (activeElement = element),
            getCanvasElementWeAreTextEditing: () => undefined,
            setCanvasElementWeAreTextEditing: () => {},
            duplicateCanvasElementBox: (element: HTMLElement) => {
                const copy = element.cloneNode(true) as HTMLElement;
                copy.removeAttribute("id");
                element.after(copy);
                return copy;
            },
            startMoving: () => {},
            stopMoving: () => {},
            setLastMoveContainer: () => {},
            resetCropBasis: () => {},
        } as unknown as ICanvasElementPointerInteractionsHost;
        interactions = new CanvasElementPointerInteractions(
            host,
            new CanvasSnapProvider(),
            new CanvasGuideProvider(),
        );
        interactions.setMouseDragHandlers(canvas);
        // The move itself happens in an animation frame, which these tests stand in for.
        vi.stubGlobal("requestAnimationFrame", () => 1);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
    });

    function mouse(type: string, init: MouseEventInit): void {
        document
            .getElementById("comical")!
            .dispatchEvent(new MouseEvent(type, { bubbles: true, ...init }));
    }

    it("is a move for a drag of the element", async () => {
        hits.bubbleContent = ce1;
        await asUserAction(() => {
            mouse("mousedown", { button: 0, buttons: 1 });
            mouse("mousemove", { buttons: 1, clientX: 20, clientY: 20 });
            // What the animation frame writes.
            ce1.style.left = "30px";
            mouse("mouseup", { clientX: 20, clientY: 20 });
        });
        expect(latestStep()).toBe("Move(SpeechBubble)");
    });

    it("is duplicating for an Alt+drag, which drags a copy", async () => {
        hits.bubbleContent = ce1;
        await asUserAction(() => {
            mouse("mousedown", { button: 0, buttons: 1, altKey: true });
            mouse("mousemove", { buttons: 1, clientX: 20, clientY: 20 });
            mouse("mouseup", { clientX: 20, clientY: 20 });
        });
        expect(page.querySelectorAll(".bloom-canvas-element")).toHaveLength(2);
        expect(latestStep()).toBe("Duplicate(SpeechBubble)");
    });

    it("is moving a tail for a drag that starts on a tail handle", async () => {
        hits.tailHandle = true;
        await asUserAction(() => {
            mouse("mousedown", { button: 0, buttons: 1 });
            // What Comical writes as the tail moves.
            ce1.setAttribute("data-bubble", "{}");
        });
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Undo.CanvasElement.MoveTail",
        );
    });

    it("is resizing a row or column for a drag on a table's boundary", async () => {
        hits.tableBoundary = true;
        await asUserAction(() => {
            mouse("mousedown", { button: 0, buttons: 1 });
            // What the table writes as the boundary moves.
            ce1.style.height = "40px";
        });
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Undo.Table.ResizeRowOrColumn",
        );
    });
});

describe("the description of a key press on the active canvas element", () => {
    let provider: CanvasElementKeyboardProvider;

    beforeEach(() => {
        provider = new CanvasElementKeyboardProvider(
            {
                deleteCurrentCanvasElement: () => ce1.remove(),
                moveActiveCanvasElement: (dx: number) => {
                    ce1.style.left = `${10 + dx}px`;
                },
                moveActiveCanvasElementInZOrder: () => {},
                getActiveCanvasElement: () => ce1,
            },
            new CanvasSnapProvider(),
        );
    });

    afterEach(() => provider.dispose());

    it.each([
        ["Delete", "Delete(SpeechBubble)"],
        ["ArrowRight", "Nudge(SpeechBubble)"],
    ])("is what %s does", async (key, description) => {
        await asUserAction(() =>
            ce1.dispatchEvent(
                new KeyboardEvent("keydown", {
                    key,
                    bubbles: true,
                    cancelable: true,
                }),
            ),
        );
        expect(latestStep()).toBe(description);
    });
});
