// Helper functions extracted from CanvasElementManager.
//
// This module encapsulates ComicalJS bubble level manipulation so other modules
// (e.g. element factories) can maintain DOM z-order and Comical hit-test order
// without importing the full CanvasElementManager.

import { Bubble, Comical } from "comicaljs";

// Adjust the levels of all the bubbles of all the listed canvas elements so that
// the one passed can be given the required level and all the others (keeping their
// current order) will be perceived by ComicalJs as having a higher level.
export const putBubbleBefore = (
    canvasElement: HTMLElement,
    canvasElementElements: HTMLElement[],
    requiredLevel: number,
): void => {
    let minLevel = Math.min(
        ...canvasElementElements.map(
            (b) => Bubble.getBubbleSpec(b as HTMLElement).level ?? 0,
        ),
    );
    if (minLevel <= requiredLevel) {
        // bump all the others up so we can insert one at requiredLevel below them all
        // We don't want to use zero as a level...some Comical code complains that
        // the canvas element doesn't have a level at all. And I'm nervous about using
        // negative numbers...something that wants a level one higher might get zero.
        canvasElementElements.forEach((b) => {
            const bubble = new Bubble(b as HTMLElement);
            const spec = bubble.getBubbleSpec();
            // the one previously at minLevel will now be at requiredLevel+1, others higher in same sequence.
            spec.level += requiredLevel - minLevel + 1;
            bubble.persistBubbleSpec();
        });
        minLevel = 2;
    }
    const bubble = new Bubble(canvasElement as HTMLElement);
    bubble.getBubbleSpec().level = requiredLevel;
    bubble.persistBubbleSpec();
    Comical.update(canvasElement.parentElement as HTMLElement);
};
