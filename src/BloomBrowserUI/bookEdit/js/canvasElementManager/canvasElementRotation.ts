// Rotation of a whole canvas element.
//
// The rotation lives in the element's own inline `transform`, in the canonical form
// `rotate(<angle>deg)`. That inline style is part of the page HTML, so the rotation is
// saved with the book and applies in the reader and in PDF output without any Bloom
// JavaScript. The inline style is therefore the single source of truth; this module owns
// the code that reads and writes it, so nothing else has to parse a transform string.
//
// The rotation is about the centre of the element (the CSS default transform-origin), which
// keeps `style.left`/`style.top` and the element's centre point unchanged. Code that
// positions an element can therefore ignore the rotation; code that converts between mouse
// positions and element coordinates cannot, and uses `rotateVector` below.
//
// Not every canvas element can rotate. A speech bubble, thought bubble, caption, rectangle
// or ellipse has its outline and tail drawn by comicaljs onto a shared SVG layer, from the
// element's un-rotated offset box. A CSS rotation of the element does not turn that drawn
// shape, so we do not offer rotation for those elements. See canRotateCanvasElement.
import { Bubble } from "comicaljs";
import { kBackgroundImageClass } from "../../toolbox/canvas/canvasElementConstants";

// Marks a rotated canvas element. The angle itself is in the inline transform; this class
// exists so that CSS and selectors can find the rotated elements cheaply.
export const kRotatedClass = "bloom-rotated";

const kRotatePattern = /rotate\(\s*(-?[0-9]*\.?[0-9]+)deg\s*\)/;

// Reduce any angle to the [0, 360) range that we store and display.
export function normalizeDegrees(degrees: number): number {
    if (!Number.isFinite(degrees)) {
        return 0;
    }
    const remainder = degrees % 360;
    return remainder < 0 ? remainder + 360 : remainder;
}

// The angle, in degrees, that this canvas element is currently rotated by. Zero when it is
// not rotated.
export function getCanvasElementRotation(canvasElement: HTMLElement): number {
    const match = kRotatePattern.exec(canvasElement.style.transform ?? "");
    if (!match) {
        return 0;
    }
    return normalizeDegrees(parseFloat(match[1]));
}

// Rotate this canvas element to the given angle. An angle of zero removes the transform, so
// an element that was never rotated, and one the user rotated back to upright, save the same
// way.
export function setCanvasElementRotation(
    canvasElement: HTMLElement,
    degrees: number,
): void {
    const normalized = normalizeDegrees(degrees);
    // Two decimal places is finer than the user can see and keeps the saved HTML short.
    const rounded = Math.round(normalized * 100) / 100;
    if (rounded === 0 || rounded === 360) {
        canvasElement.style.transform = "";
        canvasElement.classList.remove(kRotatedClass);
    } else {
        canvasElement.style.transform = `rotate(${rounded}deg)`;
        canvasElement.classList.add(kRotatedClass);
    }
}

// True if we offer rotation for this canvas element.
export function canRotateCanvasElement(canvasElement: HTMLElement): boolean {
    // The background image fills its bloom-canvas and cannot be moved or resized, so turning
    // the box makes no sense. The Rotate Right command turns the picture inside the box
    // instead; see rotateImageContentRight in imageContentTransform.ts.
    if (canvasElement.classList.contains(kBackgroundImageClass)) {
        return false;
    }
    // comicaljs draws these shapes axis-aligned; see the note at the top of this file.
    const style = Bubble.getBubbleSpec(canvasElement).style;
    return !style || style === "none";
}

// Rotate the vector (x, y) by the given angle, clockwise, which is the direction CSS
// `rotate()` turns in a document whose y axis points down.
export function rotateVector(
    x: number,
    y: number,
    degrees: number,
): { x: number; y: number } {
    if (degrees === 0) {
        return { x, y };
    }
    const radians = (degrees * Math.PI) / 180;
    const cos = Math.cos(radians);
    const sin = Math.sin(radians);
    return {
        x: x * cos - y * sin,
        y: x * sin + y * cos,
    };
}

// Convert a vector measured on the screen into the element's own un-rotated coordinates.
// Drag code computes sizes and positions in un-rotated coordinates, so it must convert the
// mouse movement first, or a rotated element grows in the wrong direction.
export function unrotateVector(
    x: number,
    y: number,
    degrees: number,
): { x: number; y: number } {
    return rotateVector(x, y, -degrees);
}

// True if the point (in the coordinate system of the element's offsetParent, which is what
// offsetLeft/offsetTop are measured in) is inside this canvas element, taking its rotation
// into account. comicaljs tests the un-rotated offset box, so the page code has to do this
// itself for rotated elements.
export function isPointInsideRotatedCanvasElement(
    canvasElement: HTMLElement,
    x: number,
    y: number,
): boolean {
    const degrees = getCanvasElementRotation(canvasElement);
    const left = canvasElement.offsetLeft;
    const top = canvasElement.offsetTop;
    const width = canvasElement.offsetWidth;
    const height = canvasElement.offsetHeight;
    const centerX = left + width / 2;
    const centerY = top + height / 2;
    // Turning the point back by the element's angle puts it in the same frame as the
    // un-rotated box, where the test is a simple rectangle containment.
    const local = unrotateVector(x - centerX, y - centerY, degrees);
    return Math.abs(local.x) <= width / 2 && Math.abs(local.y) <= height / 2;
}
