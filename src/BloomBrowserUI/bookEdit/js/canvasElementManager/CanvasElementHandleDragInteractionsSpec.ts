import { describe, it, expect } from "vitest";
import {
    clampCropPosition,
    getCroppedSides,
    getShownContentRectangle,
} from "./CanvasElementHandleDragInteractions";

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

    it("marks the sides the element really hides for a turned picture", () => {
        // The same crop after a quarter turn, as Rotate Right leaves it: a box 720 by 480 at
        // (-120, -80) in an element 480 by 320. The box hangs outside on all four sides in its
        // own coordinates, but the turn shows a rectangle 480 by 720, which fits the element's
        // width exactly and hangs out above and below.
        expect(getCroppedSides(480, 320, -120, -80, 720, 480, 1)).toEqual({
            n: true,
            e: false,
            s: true,
            w: false,
        });
    });

    it("treats a half turn like no turn, because it swaps nothing", () => {
        const upright = getCroppedSides(480, 720, -300, 0, 1080, 720, 0);
        const halfTurned = getCroppedSides(480, 720, -300, 0, 1080, 720, 2);
        expect(halfTurned).toEqual(upright);
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
    it("returns the box itself when the picture is not turned", () => {
        expect(getShownContentRectangle(-300, 0, 1080, 720, 0)).toEqual({
            left: -300,
            top: 0,
            width: 1080,
            height: 720,
        });
    });

    it("swaps the two dimensions about the centre for a quarter turn", () => {
        // The layout Rotate Right leaves on a cropped page background: a box 720 by 480 at
        // (-120, -80). The turn is about the box's own centre, at (240, 160), so the element
        // shows 480 by 720 there, which starts at the element's left edge and reaches 200
        // above its top.
        expect(getShownContentRectangle(-120, -80, 720, 480, 1)).toEqual({
            left: 0,
            top: -200,
            width: 480,
            height: 720,
        });
    });

    it("returns the box itself for a half turn, which swaps nothing", () => {
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

    it("uses the turned rectangle, not the box, for a turned picture", () => {
        // Element 480 by 320, holding a box 720 by 480 given a quarter turn, so the element
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

    it("lets a turned picture move where the box's own numbers would pin it", () => {
        // The bug this fixes: measuring the box rather than what the element shows made the
        // limits cross each other, and the same position came back for every drag.
        const high = clampCropPosition(480, 320, 0, -100, 720, 480, 1);
        const low = clampCropPosition(480, 320, 0, 100, 720, 480, 1);
        expect(high.top).toBe(-100);
        expect(low.top).toBe(100);
        expect(high.top).not.toBe(low.top);
    });
});
