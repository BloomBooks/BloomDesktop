import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";
import {
    getAllDraggables,
    isDraggable,
    kDraggableIdAttribute,
} from "../../toolbox/canvas/canvasElementDraggables";
import { adjustTarget } from "../../toolbox/games/GameTool";
import { syncBubbleLevelsToDomOrder } from "./CanvasElementZOrder";

// Adjust the ordering of canvas elements so that draggables are at the end.
export function adjustCanvasElementOrdering(
    bloomCanvases: HTMLElement[],
): void {
    bloomCanvases.forEach((bloomCanvas) => {
        const canvasElements = Array.from(
            bloomCanvas.getElementsByClassName(kCanvasElementClass),
        );
        const draggables = canvasElements.filter((b) => isDraggable(b));
        if (
            draggables.length === 0 ||
            canvasElements.indexOf(draggables[0]) ===
                canvasElements.length - draggables.length
        ) {
            return;
        }
        draggables.forEach((draggable) => {
            draggable.parentElement?.appendChild(draggable);
        });
        // The draggables are now on top in the DOM; give them the bubble levels to match.
        syncBubbleLevelsToDomOrder(bloomCanvas);
    });
}

export function adjustDraggableTarget(
    draggable: HTMLElement | undefined,
): void {
    if (!draggable) {
        adjustTarget(document.firstElementChild as HTMLElement, undefined);
        return;
    }
    const targetId = draggable.getAttribute(kDraggableIdAttribute);
    const target = targetId
        ? document.querySelector(`[data-target-of="${targetId}"]`)
        : undefined;
    adjustTarget(draggable, target as HTMLElement);
}

export function removeDetachedTargets(): void {
    const detachedTargets = Array.from(
        document.querySelectorAll("[data-target-of]"),
    );
    const canvasElements = getAllDraggables(document);
    canvasElements.forEach((canvasElement) => {
        const draggableId = canvasElement.getAttribute(kDraggableIdAttribute);
        if (draggableId) {
            const index = detachedTargets.findIndex(
                (target: Element) =>
                    target.getAttribute("data-target-of") === draggableId,
            );
            if (index > -1) {
                detachedTargets.splice(index, 1);
            }
        }
    });
    detachedTargets.forEach((target) => {
        target.remove();
    });
}
