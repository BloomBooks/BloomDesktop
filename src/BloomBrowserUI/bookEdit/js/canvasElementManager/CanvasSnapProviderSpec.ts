import { describe, it, expect } from "vitest";
import { CanvasSnapProvider } from "./CanvasSnapProvider";

// The rotation handle asks for the snapped angle on every pointer move. The rule is: snap to
// every 45 degrees when the pointer is within 14 degrees of one of them, unless CTRL is held.
describe("CanvasSnapProvider.getSnappedRotation", () => {
    const snapProvider = new CanvasSnapProvider();
    const ctrlEvent = { ctrlKey: true } as unknown as MouseEvent;
    const plainEvent = { ctrlKey: false } as unknown as MouseEvent;

    it("snaps to an angle it is close to", () => {
        expect(snapProvider.getSnappedRotation(43, plainEvent)).toBe(45);
        expect(snapProvider.getSnappedRotation(88, plainEvent)).toBe(90);
        expect(snapProvider.getSnappedRotation(4, plainEvent)).toBe(0);
    });

    it("leaves an angle that is not close to one of them", () => {
        expect(snapProvider.getSnappedRotation(20, plainEvent)).toBe(20);
        expect(snapProvider.getSnappedRotation(70, plainEvent)).toBe(70);
    });

    it("snaps at exactly the edge of the tolerance", () => {
        expect(snapProvider.getSnappedRotation(31, plainEvent)).toBe(45);
        expect(snapProvider.getSnappedRotation(59, plainEvent)).toBe(45);
    });

    it("does not snap just outside the tolerance", () => {
        expect(snapProvider.getSnappedRotation(30.5, plainEvent)).toBe(30.5);
        expect(snapProvider.getSnappedRotation(59.5, plainEvent)).toBe(59.5);
    });

    it("leaves the angle alone while CTRL is held", () => {
        // Sanity check: this angle does snap without CTRL.
        expect(snapProvider.getSnappedRotation(43, plainEvent)).toBe(45);

        expect(snapProvider.getSnappedRotation(43, ctrlEvent)).toBe(43);
    });

    it("snaps when there is no event at all", () => {
        expect(snapProvider.getSnappedRotation(43, undefined)).toBe(45);
    });

    it("snaps a negative angle", () => {
        // toBeCloseTo, because rounding a negative angle to zero gives negative zero,
        // which is zero everywhere it matters but is not Object.is equal to it.
        expect(snapProvider.getSnappedRotation(-5, plainEvent)).toBeCloseTo(0);
        expect(snapProvider.getSnappedRotation(-43, plainEvent)).toBe(-45);
    });

    it("snaps an angle near a whole turn to the whole turn", () => {
        // 360 rather than 0: the caller brings the angle into range when it writes it.
        expect(snapProvider.getSnappedRotation(359, plainEvent)).toBe(360);
    });
});
