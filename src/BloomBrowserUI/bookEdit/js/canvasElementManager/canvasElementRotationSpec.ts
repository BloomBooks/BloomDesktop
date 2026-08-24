import { describe, it, expect, vi } from "vitest";

// The real comicaljs Bubble reads the data-bubble attribute of the element and does a lot
// else besides. canRotateCanvasElement wants only the bubble style, so supply that much,
// from an attribute that the tests set.
vi.mock("comicaljs", () => ({
    Bubble: {
        getBubbleSpec: (element: HTMLElement) => ({
            style: element.getAttribute("data-test-bubble-style") ?? undefined,
        }),
    },
}));

// This import deliberately comes after the vi.mock call above, so that the module gets the
// stubbed comicaljs.
import {
    canRotateCanvasElement,
    getCanvasElementRotation,
    getHandleCursorForRotation,
    isPointInsideRotatedCanvasElement,
    kRotatedClass,
    normalizeDegrees,
    rotateVector,
    setCanvasElementRotation,
    unrotateVector,
} from "./canvasElementRotation";

function makeCanvasElement(): HTMLElement {
    return document.createElement("div");
}

// jsdom lays nothing out, so offsetLeft and its friends are all zero. Give the element the
// box that the test wants.
function giveElementABox(
    element: HTMLElement,
    left: number,
    top: number,
    width: number,
    height: number,
): void {
    Object.defineProperty(element, "offsetLeft", { value: left });
    Object.defineProperty(element, "offsetTop", { value: top });
    Object.defineProperty(element, "offsetWidth", { value: width });
    Object.defineProperty(element, "offsetHeight", { value: height });
}

describe("normalizeDegrees", () => {
    it("leaves an angle that is already in range", () => {
        expect(normalizeDegrees(0)).toBe(0);
        expect(normalizeDegrees(45)).toBe(45);
        expect(normalizeDegrees(359)).toBe(359);
    });

    it("brings an angle of 360 or more into range", () => {
        expect(normalizeDegrees(360)).toBe(0);
        expect(normalizeDegrees(370)).toBe(10);
        expect(normalizeDegrees(725)).toBe(5);
    });

    it("brings a negative angle into range", () => {
        expect(normalizeDegrees(-90)).toBe(270);
        // toBeCloseTo, because a remainder of zero from a negative number is negative
        // zero, which is zero everywhere it matters but is not Object.is equal to it.
        expect(normalizeDegrees(-360)).toBeCloseTo(0);
        expect(normalizeDegrees(-450)).toBe(270);
    });

    it("gives zero for an angle that is not a number", () => {
        expect(normalizeDegrees(NaN)).toBe(0);
        expect(normalizeDegrees(Infinity)).toBe(0);
    });
});

describe("getCanvasElementRotation", () => {
    it("gives zero for an element with no transform", () => {
        expect(getCanvasElementRotation(makeCanvasElement())).toBe(0);
    });

    it("gives zero for a transform that holds no rotation", () => {
        const element = makeCanvasElement();
        element.style.transform = "scale(2)";
        expect(getCanvasElementRotation(element)).toBe(0);
    });

    it("reads the angle out of the transform", () => {
        const element = makeCanvasElement();
        element.style.transform = "rotate(45deg)";
        expect(getCanvasElementRotation(element)).toBe(45);
    });

    it("reads the angle when another function comes first", () => {
        const element = makeCanvasElement();
        element.style.transform = "translate(5px, 5px) rotate(90deg)";
        expect(getCanvasElementRotation(element)).toBe(90);
    });

    it("brings a negative saved angle into range", () => {
        const element = makeCanvasElement();
        element.style.transform = "rotate(-30deg)";
        expect(getCanvasElementRotation(element)).toBe(330);
    });
});

describe("setCanvasElementRotation", () => {
    it("writes the angle and marks the element", () => {
        const element = makeCanvasElement();
        // Sanity check: nothing is set before the call.
        expect(element.style.transform).toBe("");
        expect(element.classList.contains(kRotatedClass)).toBe(false);

        setCanvasElementRotation(element, 45);

        expect(element.style.transform).toBe("rotate(45deg)");
        expect(element.classList.contains(kRotatedClass)).toBe(true);
        expect(getCanvasElementRotation(element)).toBe(45);
    });

    it("brings a negative angle into range", () => {
        const element = makeCanvasElement();
        setCanvasElementRotation(element, -30);
        expect(element.style.transform).toBe("rotate(330deg)");
    });

    it("keeps two decimal places", () => {
        const element = makeCanvasElement();
        setCanvasElementRotation(element, 12.347);
        expect(element.style.transform).toBe("rotate(12.35deg)");
    });

    it("removes the transform and the mark for an angle of zero", () => {
        const element = makeCanvasElement();
        setCanvasElementRotation(element, 90);
        // Sanity check: the element really is rotated before we straighten it.
        expect(element.classList.contains(kRotatedClass)).toBe(true);

        setCanvasElementRotation(element, 0);

        expect(element.style.transform).toBe("");
        expect(element.classList.contains(kRotatedClass)).toBe(false);
    });

    it("treats a whole turn as no rotation at all", () => {
        const element = makeCanvasElement();
        setCanvasElementRotation(element, 360);
        expect(element.style.transform).toBe("");
        expect(element.classList.contains(kRotatedClass)).toBe(false);
    });
});

describe("canRotateCanvasElement", () => {
    it("allows a plain canvas element", () => {
        expect(canRotateCanvasElement(makeCanvasElement())).toBe(true);
    });

    it("refuses the background image", () => {
        const element = makeCanvasElement();
        element.classList.add("bloom-backgroundImage");
        expect(canRotateCanvasElement(element)).toBe(false);
    });

    it("refuses a shape that comicaljs draws", () => {
        const element = makeCanvasElement();
        element.setAttribute("data-test-bubble-style", "speech");
        expect(canRotateCanvasElement(element)).toBe(false);
    });

    it("allows an element whose bubble style is none", () => {
        const element = makeCanvasElement();
        element.setAttribute("data-test-bubble-style", "none");
        expect(canRotateCanvasElement(element)).toBe(true);
    });
});

describe("rotateVector and unrotateVector", () => {
    it("gives back the same vector for an angle of zero", () => {
        expect(rotateVector(3, 4, 0)).toEqual({ x: 3, y: 4 });
    });

    it("turns clockwise, which is down the screen from the x axis", () => {
        const turned = rotateVector(10, 0, 90);
        expect(turned.x).toBeCloseTo(0);
        expect(turned.y).toBeCloseTo(10);
    });

    it("turns half a turn", () => {
        const turned = rotateVector(10, 5, 180);
        expect(turned.x).toBeCloseTo(-10);
        expect(turned.y).toBeCloseTo(-5);
    });

    it("undoes the turn", () => {
        const turned = rotateVector(7, -3, 37);
        // Sanity check: the turn really moved the vector.
        expect(turned.x).not.toBeCloseTo(7);

        const back = unrotateVector(turned.x, turned.y, 37);

        expect(back.x).toBeCloseTo(7);
        expect(back.y).toBeCloseTo(-3);
    });
});

describe("isPointInsideRotatedCanvasElement", () => {
    it("tests the box itself when the element is not rotated", () => {
        const element = makeCanvasElement();
        giveElementABox(element, 100, 100, 100, 20);
        expect(isPointInsideRotatedCanvasElement(element, 195, 110)).toBe(true);
        expect(isPointInsideRotatedCanvasElement(element, 150, 155)).toBe(
            false,
        );
    });

    it("tests the turned box when the element is rotated", () => {
        const element = makeCanvasElement();
        giveElementABox(element, 100, 100, 100, 20);
        setCanvasElementRotation(element, 90);
        // A quarter turn about the centre (150, 110) puts the long side up and down the
        // screen, so a point above the centre is now inside and one beside it is not.
        expect(isPointInsideRotatedCanvasElement(element, 150, 155)).toBe(true);
        expect(isPointInsideRotatedCanvasElement(element, 195, 110)).toBe(
            false,
        );
    });

    it("takes the centre of the element as inside whatever the angle", () => {
        const element = makeCanvasElement();
        giveElementABox(element, 100, 100, 100, 20);
        setCanvasElementRotation(element, 37);
        expect(isPointInsideRotatedCanvasElement(element, 150, 110)).toBe(true);
    });
});

describe("getHandleCursorForRotation", () => {
    it("gives each handle its own cursor on an upright element", () => {
        expect(getHandleCursorForRotation("n", 0)).toBe("ns-resize");
        expect(getHandleCursorForRotation("s", 0)).toBe("ns-resize");
        expect(getHandleCursorForRotation("e", 0)).toBe("ew-resize");
        expect(getHandleCursorForRotation("w", 0)).toBe("ew-resize");
        expect(getHandleCursorForRotation("nw", 0)).toBe("nw-resize");
        expect(getHandleCursorForRotation("ne", 0)).toBe("ne-resize");
        expect(getHandleCursorForRotation("se", 0)).toBe("se-resize");
        expect(getHandleCursorForRotation("sw", 0)).toBe("sw-resize");
    });

    it("swaps the two axes of the sides at a quarter turn", () => {
        expect(getHandleCursorForRotation("n", 90)).toBe("ew-resize");
        expect(getHandleCursorForRotation("e", 90)).toBe("ns-resize");
    });

    it("gives a side the diagonal its axis really lies on at 45 degrees", () => {
        // A vertical axis turned 45 degrees clockwise runs from the bottom left to the top
        // right, so the north handle takes the north-east to south-west cursor.
        expect(getHandleCursorForRotation("n", 45)).toBe("nesw-resize");
        expect(getHandleCursorForRotation("e", 45)).toBe("nwse-resize");
    });

    it("moves a corner round to the direction the turn takes it to", () => {
        expect(getHandleCursorForRotation("nw", 90)).toBe("ne-resize");
        expect(getHandleCursorForRotation("nw", 45)).toBe("n-resize");
        expect(getHandleCursorForRotation("se", 180)).toBe("nw-resize");
    });

    it("takes an angle to the nearest eighth of a turn", () => {
        expect(getHandleCursorForRotation("n", 4)).toBe("ns-resize");
        expect(getHandleCursorForRotation("n", 88)).toBe("ew-resize");
    });

    it("treats a whole turn as no turn", () => {
        expect(getHandleCursorForRotation("nw", 360)).toBe("nw-resize");
        expect(getHandleCursorForRotation("n", 720)).toBe("ns-resize");
    });
});
