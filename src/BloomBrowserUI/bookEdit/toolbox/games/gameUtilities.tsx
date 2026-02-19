// This file is a work-in-progress, trying to isolate game-related functionality that
// is needed both in the toolbox and in the content page.

import { getAllDraggables, isDraggable } from "../../js/CanvasElementManager";

export function doesContainingPageHaveSameSizeMode(
    refElt: HTMLElement,
): boolean {
    const page = refElt.closest(".bloom-page") as HTMLElement;
    return doesPageHaveSameSizeMode(page);
}

function doesPageHaveSameSizeMode(
    page: HTMLElement | null | undefined,
): boolean {
    if (!page) {
        return false;
    }
    return page.getAttribute("data-same-size") !== "false";
}

// Returns true if we need to take into account that this element must be kept the same size
// as other elements. This means that (a) it's on a page that has this behavior,
// (b) it is an element that obeys this constraint (currently has data-draggable-id attribute),
// and (c) there is at least one other element in the group that it must match.
export function needsToBeKeptSameSize(elt: HTMLElement): boolean {
    if (!isDraggable(elt)) {
        return false;
    }
    const page = elt.closest(".bloom-page") as HTMLElement;
    if (!doesPageHaveSameSizeMode(page)) {
        return false;
    }
    return getAllDraggables(page).length > 1;
}
