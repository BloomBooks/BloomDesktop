import { describe, it, expect } from "vitest";
import { screenDeltaToElementDelta } from "./CanvasElementHandleDragInteractions";

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
