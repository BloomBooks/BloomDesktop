import { describe, it, expect } from "vitest";
import { getCroppedSides } from "./CanvasElementHandleDragInteractions";

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
