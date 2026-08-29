// Marks the translation groups whose box is too small to hold the affordances Bloom
// normally draws inside a text box: the little grey language name in the corner, and the
// format cog of whichever editable has focus. A calendar month grid has forty-nine cells
// of a few square centimetres each, and in a box that size those two cover the text the
// user is trying to read.
//
// This module only marks; the editMode.less rules keyed on the class do the hiding, and
// SmallTranslationGroupToolbar.tsx puts the same two affordances just below the box
// instead.

/**
 * The class a translation group carries while its box is too small for the language name
 * and the format cog to be drawn inside it.
 */
export const kTooSmallForInBoxAffordancesClass =
    "bloom-tooSmallForInBoxAffordances";

// A box shorter than this has no room for a line of text and a language name under it,
// and one narrower than this has no room for a language name beside the cog. Both are in
// layout pixels, which is what offsetHeight and offsetWidth report: they are the size the
// box has in the page's own coordinates, unaffected by the transform that zooms the page,
// so the same box is marked at every zoom level.
export const kMinHeightForInBoxAffordances = 60;
export const kMinWidthForInBoxAffordances = 120;

/** True if this group's box is too small for the in-box language name and format cog. */
export function isTooSmallForInBoxAffordances(group: HTMLElement): boolean {
    return (
        group.offsetHeight < kMinHeightForInBoxAffordances ||
        group.offsetWidth < kMinWidthForInBoxAffordances
    );
}

/** Add or remove one group's marker class, according to the size it has now. */
export function updateInBoxAffordanceMarking(group: HTMLElement): void {
    group.classList.toggle(
        kTooSmallForInBoxAffordancesClass,
        isTooSmallForInBoxAffordances(group),
    );
}

// The one ResizeObserver watching every translation group of the page. It is page-wide
// rather than per-group so that the whole thing can be taken down in one call when we
// leave the page.
let groupSizeObserver: ResizeObserver | undefined;

/**
 * Mark the translation groups within `container` for their current size, and keep the
 * marking current as they change size.
 *
 * A group changes size long after the page loads: an origami splitter drag, a table row
 * or column resize, a canvas element being moved or resized, a layout change. The
 * observer is what makes the affordances come back when a box grows and go away again
 * when it shrinks.
 *
 * Safe to call again on the same groups: observing a group that is already observed does
 * nothing, so a later pass over the page costs nothing.
 */
export function observeTranslationGroupSizes(container: HTMLElement): void {
    if (!groupSizeObserver) {
        groupSizeObserver = new ResizeObserver((entries) =>
            entries.forEach((entry) =>
                updateInBoxAffordanceMarking(entry.target as HTMLElement),
            ),
        );
    }
    groupsWithin(container).forEach((group) => {
        updateInBoxAffordanceMarking(group);
        groupSizeObserver!.observe(group);
    });
}

/**
 * Stop watching, and take the marker class off every group within `container`.
 *
 * The class sits on the bloom-translationGroup, which is part of what Bloom saves, so it
 * has to come off before the page content is captured. Called from removeEditingDebris in
 * bloomEditing.ts.
 */
export function stopObservingTranslationGroupSizes(
    container: HTMLElement,
): void {
    groupSizeObserver?.disconnect();
    groupSizeObserver = undefined;
    groupsWithin(container).forEach((group) =>
        group.classList.remove(kTooSmallForInBoxAffordancesClass),
    );
}

/** Every translation group at or below `container`. */
function groupsWithin(container: HTMLElement): HTMLElement[] {
    const groups = Array.from(
        container.querySelectorAll<HTMLElement>(".bloom-translationGroup"),
    );
    // A table hands us one new cell at a time, and that cell may be the group itself.
    if (container.classList.contains("bloom-translationGroup"))
        groups.unshift(container);
    return groups;
}
