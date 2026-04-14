import { copyContentToTarget, getTarget } from "bloom-player";

// Keep UI-only selection state out of target shadow content.
export function copyContentToTargetAndCleanup(
    draggableElement: HTMLElement,
): void {
    copyContentToTarget(draggableElement);

    const target = getTarget(draggableElement);
    if (!target) {
        return;
    }

    target.querySelectorAll(".bloom-selected").forEach((el) => {
        el.classList.remove("bloom-selected");
    });
}
