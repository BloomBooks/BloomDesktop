// This file is a work-in-progress, trying to isolate game-related functionality that
// is needed both in the toolbox and in the content page.

export function doesContainingPageHaveSameSizeMode(
    refElt: HTMLElement
): boolean {
    const page = refElt.closest(".bloom-page") as HTMLElement;
    return doesPageHaveSameSizeMode(page);
}

function doesPageHaveSameSizeMode(
    page: HTMLElement | null | undefined
): boolean {
    if (!page) {
        return false;
    }
    // Don't support same-size mode for this game. The new source page sets this false, but earlier
    // ones just didn't have it, since same-size is usually the default.
    return (
        page.getAttribute("data-same-size") !== "false" &&
        page.getAttribute("data-activity") !== "drag-letter-to-target"
    );
}

// Returns true if we need to take into account that this element must be kept the same size
// as other elements. This means that (a) it's on a page that has this behavior,
// (b) it is an element that obeys this constraint (currently has data-draggable-id attribute),
// and (c) there is at least one other element in the group that it must match.
export function needsToBeKeptSameSize(elt: HTMLElement): boolean {
    if (!elt.hasAttribute("data-draggable-id")) {
        return false;
    }
    const page = elt.closest(".bloom-page") as HTMLElement;
    if (!doesPageHaveSameSizeMode(page)) {
        return false;
    }
    return page.querySelectorAll("[data-draggable-id]").length > 1;
}
