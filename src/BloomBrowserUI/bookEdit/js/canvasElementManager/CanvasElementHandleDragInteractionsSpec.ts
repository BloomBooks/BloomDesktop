import { describe, it, expect } from "vitest";
import {
    clampCropPosition,
    getCroppedSides,
    getShownContentRectangle,
    screenDeltaToElementDelta,
    shownPictureIsBiggerThanElement,
} from "./CanvasElementHandleDragInteractions";

describe("screenDeltaToElementDelta", () => {
    it("passes a movement straight through at 100% zoom with no rotation", () => {
        expect(screenDeltaToElementDelta(30, -12, 0, 1)).toEqual({
            x: 30,
            y: -12,
        });
    });

    it("shrinks a movement by the zoom, so the dragged edge stays under the pointer", () => {
        // At 150% the page is drawn half as big again, so 60 screen pixels are 40 of the
        // element's own pixels. Using the screen pixels as they are made the edge run ahead of
        // the pointer by half as much again.
        const delta = screenDeltaToElementDelta(60, 30, 0, 1.5);
        expect(delta.x).toBeCloseTo(40);
        expect(delta.y).toBeCloseTo(20);
    });

    it("both undoes the zoom and turns the screen axes into a rotated element's own", () => {
        // An element rotated 30 degrees at 150%: a pointer movement of 45 style pixels along the
        // element's own x axis shows on screen as 67.5 pixels in the direction of 30 degrees.
        const angle = (30 * Math.PI) / 180;
        const screenX = 67.5 * Math.cos(angle);
        const screenY = 67.5 * Math.sin(angle);
        // Sanity check: the screen movement is not along a screen axis.
        expect(Math.abs(screenY)).toBeGreaterThan(1);
        const delta = screenDeltaToElementDelta(screenX, screenY, 30, 1.5);
        expect(delta.x).toBeCloseTo(45);
        expect(delta.y).toBeCloseTo(0);
    });
});

describe("shownPictureIsBiggerThanElement", () => {
    it("does not call an uncropped picture rotated 90 degrees cropped", () => {
        // A landscape picture 400 by 300, rotated 90 degrees on the page background, as Rotate
        // Right leaves it: the element is 300 wide and 400 tall, and the picture's box is still
        // 400 by 300 and lies across it.
        const boxWidth = 400;
        const elementWidth = 300;
        // Sanity check: the box alone is wider than the element, which is what a comparison
        // that ignores the rotation would take for a crop.
        expect(boxWidth).toBeGreaterThan(elementWidth + 1);
        expect(
            shownPictureIsBiggerThanElement(
                elementWidth,
                400,
                boxWidth,
                300,
                1,
            ),
        ).toBe(false);
        expect(
            shownPictureIsBiggerThanElement(elementWidth, 400, 400, 300, 3),
        ).toBe(false);
    });

    it("still sees a crop of a picture rotated 90 degrees", () => {
        // The same picture drawn twice as big: it shows 600 by 800 in an element 300 by 400.
        expect(shownPictureIsBiggerThanElement(300, 400, 800, 600, 1)).toBe(
            true,
        );
    });

    it("sees a crop of a picture that is not rotated", () => {
        expect(shownPictureIsBiggerThanElement(480, 720, 1080, 720, 0)).toBe(
            true,
        );
        expect(shownPictureIsBiggerThanElement(480, 320, 480, 320, 0)).toBe(
            false,
        );
    });
});

describe("getCroppedSides", () => {
    it("says no side is cropped when the picture just fills its element", () => {
        expect(getCroppedSides(480, 320, 0, 0, 480, 320, 0)).toEqual({
            n: false,
            e: false,
            s: false,
            w: false,
        });
    });

    it("marks the two sides a crop of the width hides", () => {
        // A picture 1080 wide in an element 480 wide, moved 300 to the left, so its left and
        // right hang outside and its top and bottom line up.
        expect(getCroppedSides(480, 720, -300, 0, 1080, 720, 0)).toEqual({
            n: false,
            e: true,
            s: false,
            w: true,
        });
    });

    it("marks the sides the element really hides for a rotated picture", () => {
        // The same crop after a 90-degree rotation, as Rotate Right leaves it: a box 720 by 480 at
        // (-120, -80) in an element 480 by 320. The box hangs outside on all four sides in its
        // own coordinates, but the rotation shows a rectangle 480 by 720, which fits the element's
        // width exactly and hangs out above and below.
        expect(getCroppedSides(480, 320, -120, -80, 720, 480, 1)).toEqual({
            n: true,
            e: false,
            s: true,
            w: false,
        });
    });

    it("treats a 180-degree rotation like no rotation, because it swaps nothing", () => {
        const upright = getCroppedSides(480, 720, -300, 0, 1080, 720, 0);
        const halfRotated = getCroppedSides(480, 720, -300, 0, 1080, 720, 2);
        expect(halfRotated).toEqual(upright);
    });

    it("does not mark a side for a difference of less than a pixel", () => {
        // Client values are whole pixels, so a fraction of a pixel is rounding, not a crop.
        expect(getCroppedSides(480, 320, -0.4, 0, 480.8, 320, 0)).toEqual({
            n: false,
            e: false,
            s: false,
            w: false,
        });
    });
});

describe("getShownContentRectangle", () => {
    it("returns the box itself when the picture is not rotated", () => {
        expect(getShownContentRectangle(-300, 0, 1080, 720, 0)).toEqual({
            left: -300,
            top: 0,
            width: 1080,
            height: 720,
        });
    });

    it("swaps the two dimensions about the centre for a 90-degree rotation", () => {
        // The layout Rotate Right leaves on a cropped page background: a box 720 by 480 at
        // (-120, -80). The rotation is about the box's own centre, at (240, 160), so the element
        // shows 480 by 720 there, which starts at the element's left edge and reaches 200
        // above its top.
        expect(getShownContentRectangle(-120, -80, 720, 480, 1)).toEqual({
            left: 0,
            top: -200,
            width: 480,
            height: 720,
        });
    });

    it("returns the box itself for a 180-degree rotation, which swaps nothing", () => {
        expect(getShownContentRectangle(-120, -80, 720, 480, 2)).toEqual({
            left: -120,
            top: -80,
            width: 720,
            height: 480,
        });
    });
});

describe("clampCropPosition", () => {
    it("leaves a position that keeps the picture covering the element", () => {
        expect(clampCropPosition(480, 720, -300, 0, 1080, 720, 0)).toEqual({
            left: -300,
            top: 0,
        });
    });

    it("stops the picture before a blank band appears at the left or the top", () => {
        // Asked for a position down and to the right of the element's own corner.
        expect(clampCropPosition(480, 720, 40, 25, 1080, 720, 0)).toEqual({
            left: 0,
            top: 0,
        });
    });

    it("stops the picture before a blank band appears at the right", () => {
        // 480 - 1080 is as far left as the picture can go and still reach the right edge.
        expect(clampCropPosition(480, 720, -900, 0, 1080, 720, 0)).toEqual({
            left: -600,
            top: 0,
        });
    });

    it("uses the rotated rectangle, not the box, for a rotated picture", () => {
        // Element 480 by 320, holding a box 720 by 480 given a 90-degree rotation, so the element
        // shows 480 by 720. The width matches the element exactly, so the only position that
        // leaves no blank band puts the box at -120. Up and down, the box may sit anywhere
        // from -280 to 120, and 200 is past that.
        expect(clampCropPosition(480, 320, 0, 200, 720, 480, 1)).toEqual({
            left: -120,
            top: 120,
        });
        expect(clampCropPosition(480, 320, 0, -400, 720, 480, 1)).toEqual({
            left: -120,
            top: -280,
        });
    });

    it("lets a rotated picture move where the box's own numbers would pin it", () => {
        // The bug this fixes: measuring the box rather than what the element shows made the
        // limits cross each other, and the same position came back for every drag.
        const high = clampCropPosition(480, 320, 0, -100, 720, 480, 1);
        const low = clampCropPosition(480, 320, 0, 100, 720, 480, 1);
        expect(high.top).toBe(-100);
        expect(low.top).toBe(100);
        expect(high.top).not.toBe(low.top);
    });
});
