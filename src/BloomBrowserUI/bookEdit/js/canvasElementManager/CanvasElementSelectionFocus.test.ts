import { afterEach, describe, expect, test, vi } from "vitest";

// Comical wants paper.js and a real <canvas>, which jsdom doesn't give us. Selecting a canvas
// element only asks Comical to activate it, which doesn't matter to what we test here.
vi.mock("comicaljs", () => ({
    Bubble: class {
        static getBubbleSpec() {
            return { version: "1.0", style: "speech", tails: [] };
        }
        getBubbleSpec() {
            return { version: "1.0", style: "speech", tails: [] };
        }
    },
    Comical: {
        setSelectorForBubblesWhichTailMidpointMayOverlap: () => {},
        activateElement: () => {},
        update: () => {},
        findAncestors: () => [],
    },
}));

// Selecting an element draws its toolbar, a React component whose hooks ask Bloom's server
// about features and languages. Unit tests must not reach the network, and the toolbar has
// nothing to do with where the focus goes.
vi.mock("./CanvasElementContextControls", () => ({
    renderCanvasElementContextControls: vi.fn(),
}));

// This import deliberately comes after the vi.mock calls above, so that the module graph
// it pulls in gets the stubbed comicaljs.
import { CanvasElementManager } from "./CanvasElementManager";

// Two speech bubbles on one canvas, each with visible text that can be typed in.
function setUpTwoTextBubbles(): HTMLElement[] {
    document.body.innerHTML = `
        <div id="page-scaling-container">
            <div class="bloom-page">
                <div class="bloom-canvas">
                    <div class="bloom-canvas-element" id="bubble1">
                        <div class="bloom-translationGroup">
                            <div class="bloom-editable bloom-visibility-code-on" lang="en"
                                 contenteditable="true" tabindex="0"><p>One</p></div>
                        </div>
                    </div>
                    <div class="bloom-canvas-element" id="bubble2">
                        <div class="bloom-translationGroup">
                            <div class="bloom-editable bloom-visibility-code-on" lang="en"
                                 contenteditable="true" tabindex="0"><p>Two</p></div>
                        </div>
                    </div>
                </div>
            </div>
        </div>`;
    return Array.from(
        document.querySelectorAll<HTMLElement>(".bloom-canvas-element"),
    );
}

const editableOf = (bubble: HTMLElement): HTMLElement =>
    bubble.querySelector(".bloom-editable") as HTMLElement;

afterEach(() => {
    document.body.innerHTML = "";
});

describe("CanvasElementManager.setActiveElement focus (BL-17006)", () => {
    test("selecting a different text bubble takes the focus out of the one being typed in", () => {
        const [bubble1, bubble2] = setUpTwoTextBubbles();
        const manager = new CanvasElementManager();

        // The user has been typing in the second bubble: it is selected and its text has focus.
        manager.setActiveElement(bubble2);
        editableOf(bubble2).focus();
        expect(document.activeElement).toBe(editableOf(bubble2));

        // A single click on the first bubble selects it. The click itself is kept from moving
        // the focus (CanvasElementPointerInteractions prevents the default of the mouse
        // events), so whatever setActiveElement does is all that decides where keys go next.
        manager.setActiveElement(bubble1);

        expect(manager.getActiveElement()).toBe(bubble1);
        // Keys must not go on into the bubble that is no longer selected.
        // (Comparing ids makes a failure say which bubble still has the focus.)
        expect(
            document.activeElement?.closest(".bloom-canvas-element")?.id,
        ).not.toBe("bubble2");
    });
});
