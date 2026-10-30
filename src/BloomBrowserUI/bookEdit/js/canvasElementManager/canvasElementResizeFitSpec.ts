// Tests that resizing a picture by a corner leaves its box fitted to the picture when the drag
// ends, so that selecting the picture afterwards writes nothing. Drives the drag through the
// control frame's corner handle.
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

// These imports deliberately come after the vi.mock call above, so that the module graph they
// pull in gets the stubbed module.
import {
    initializeCanvasElementManager,
    theOneCanvasElementManager,
} from "./CanvasElementManager";

// The picture's natural size. Its box starts out fitted to it: 133.2 / 100 = 333 / 250.
const kNaturalWidth = 333;
const kNaturalHeight = 250;

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
    simulateLayout(document.getElementById("picture")!);
    simulateLayout(document.getElementById("ce1")!);
    Object.defineProperties(document.getElementById("pictureImg")!, {
        naturalWidth: { get: () => kNaturalWidth },
        naturalHeight: { get: () => kNaturalHeight },
    });
    initializeCanvasElementManager();
    theOneCanvasElementManager.isCanvasElementEditingOn = true;
    theOneCanvasElementManager.setActiveElement(undefined);
});

afterEach(() => {
    document.body.innerHTML = "";
});

// Drag the selected element's se corner handle by (dx, dy).
function dragSeCorner(dx: number, dy: number): void {
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
    it("ends with the box fitted to the picture, so selecting the picture later changes nothing", () => {
        const picture = document.getElementById("picture")!;
        const ce1 = document.getElementById("ce1")!;
        const before = geometry(picture);

        // Selecting a picture whose box fits it already writes nothing.
        theOneCanvasElementManager.setActiveElement(picture);
        expect(geometry(picture)).toEqual(before);

        dragSeCorner(40, 30);

        const resized = geometry(picture);
        // Sanity check: the drag changed the size.
        expect(resized.width).not.toBe(before.width);
        // The box the resize left fits the picture.
        const resizedRatio =
            parseFloat(resized.width) / parseFloat(resized.height);
        expect(resizedRatio).toBeCloseTo(kNaturalWidth / kNaturalHeight, 6);

        theOneCanvasElementManager.setActiveElement(ce1);
        theOneCanvasElementManager.setActiveElement(picture);

        expect(geometry(picture)).toEqual(resized);
    });
});
