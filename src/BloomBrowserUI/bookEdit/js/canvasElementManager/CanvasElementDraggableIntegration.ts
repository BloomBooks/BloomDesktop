import { Bubble, Comical } from "comicaljs";
import { kCanvasElementClass } from "../../toolbox/canvas/canvasElementConstants";
import {
    getAllDraggables,
    isDraggable,
    kDraggableIdAttribute,
} from "../../toolbox/canvas/canvasElementDraggables";
import { adjustTarget } from "../../toolbox/games/GameTool";

export interface ICanvasElementDraggableIntegrationHost {
    getAllBloomCanvasesOnPage: () => HTMLElement[];
}

export class CanvasElementDraggableIntegration {
    private host: ICanvasElementDraggableIntegrationHost;

    public constructor(host: ICanvasElementDraggableIntegrationHost) {
        this.host = host;
    }

    // Adjust the ordering of canvas elements so that draggables are at the end.
    public adjustCanvasElementOrdering = (): void => {
        const bloomCanvases = this.host.getAllBloomCanvasesOnPage();
        bloomCanvases.forEach((bloomCanvas) => {
            const canvasElements = Array.from(
                bloomCanvas.getElementsByClassName(kCanvasElementClass),
            );
            let maxLevel = Math.max(
                ...canvasElements.map(
                    (b) => Bubble.getBubbleSpec(b as HTMLElement).level ?? 0,
                ),
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
                const bubble = new Bubble(draggable as HTMLElement);
                bubble.getBubbleSpec().level = maxLevel + 1;
                bubble.persistBubbleSpec();
                maxLevel++;
            });
            Comical.update(bloomCanvas);
        });
    };

    public adjustTarget = (draggable: HTMLElement | undefined): void => {
        if (!draggable) {
            adjustTarget(document.firstElementChild as HTMLElement, undefined);
            return;
        }
        const targetId = draggable.getAttribute(kDraggableIdAttribute);
        const target = targetId
            ? document.querySelector(`[data-target-of="${targetId}"]`)
            : undefined;
        adjustTarget(draggable, target as HTMLElement);
    };

    public removeDetachedTargets = (): void => {
        const detachedTargets = Array.from(
            document.querySelectorAll("[data-target-of]"),
        );
        const canvasElements = getAllDraggables(document);
        canvasElements.forEach((canvasElement) => {
            const draggableId = canvasElement.getAttribute(
                kDraggableIdAttribute,
            );
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
    };
}
