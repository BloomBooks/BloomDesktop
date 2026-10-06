import { kSelectorForPotentialNiceScrollElements } from "bloom-player";

// The elements niceScroll inserts. Each rail contains a cursor (the thumb); we list both so a
// stray one can't survive.
const kNiceScrollInsertedElementSelector =
    ".nicescroll-rails, .nicescroll-cursors";

// The alignment classes bloom-player's addScrollbarsToPage() takes off a translationGroup before
// applying niceScroll, leaving a "<name>-removed" marker in their place so they can be restored.
const kVerticalAlignClassesRemovedForNiceScroll = [
    "bloom-vertical-align-center",
    "bloom-vertical-align-bottom",
];

/**
 * Undo, within 'root', everything that giving an overflowing text box a scroll bar did to the page,
 * so that none of it gets saved into the book.
 *
 * Unlike bloom-player's cleanupNiceScroll(), which asks each live niceScroll instance to remove
 * itself, this works on any root, including a detached clone, so saving leaves the live page alone.
 *
 * There are three kinds of leftovers:
 *
 * 1. The .nicescroll-rails divs (each containing a .nicescroll-cursors div). niceScroll appends
 *    them to the nearest positioned or scrollable ancestor, which can be inside the page (e.g. an
 *    origami split-pane component). Rails appended to the body are outside the page, so never saved.
 *
 * 2. Classes addScrollbarsToPage() changed, because niceScroll does not work with the display:flex
 *    of vertical alignment: bloom-vertical-align-center/-bottom moved aside to a "-removed" marker
 *    on the translationGroup, and scrolling-bubble added to a canvas element's editable. Saving
 *    these would silently lose the user's vertical alignment choice.
 *
 * 3. Inline styles niceScroll sets on the box it scrolls without recording them for restoring:
 *    overflow-x/-y, outline, and a pixel width (a Chrome workaround it does not always undo,
 *    BL-14052). (position:relative is set only in the two-argument niceScroll() form, which
 *    bloom-player does not use.)
 */
export function removeNiceScrollArtifacts(root: HTMLElement): void {
    for (const inserted of Array.from(
        root.querySelectorAll(kNiceScrollInsertedElementSelector),
    )) {
        inserted.remove();
    }

    for (const alignClass of kVerticalAlignClassesRemovedForNiceScroll) {
        const removedMarker = alignClass + "-removed";
        // getElementsByClassName is live and we remove the class it selects on, so copy it first.
        for (const translationGroup of Array.from(
            root.getElementsByClassName(removedMarker),
        )) {
            translationGroup.classList.remove(removedMarker);
            translationGroup.classList.add(alignClass);
        }
    }

    for (const scrollingBubble of Array.from(
        root.getElementsByClassName("scrolling-bubble"),
    )) {
        scrollingBubble.classList.remove("scrolling-bubble");
    }

    for (const scrollBox of Array.from(
        root.querySelectorAll<HTMLElement>(
            kSelectorForPotentialNiceScrollElements,
        ),
    )) {
        // An inline overflow-y is niceScroll's fingerprint (nothing in Bloom sets one). Checking
        // for it means we can't blank an author's inline width on a box niceScroll never touched.
        if (!scrollBox.style.overflowY) {
            continue;
        }
        // Longhands named explicitly: whether clearing a shorthand clears its longhands varies
        // between CSSOM implementations (jsdom, where our tests run, does not).
        for (const property of [
            "overflow",
            "overflow-x",
            "overflow-y",
            "outline",
            "width",
        ]) {
            scrollBox.style.removeProperty(property);
        }
        if (!scrollBox.getAttribute("style")) {
            // Don't leave an empty style attribute behind in the saved HTML.
            scrollBox.removeAttribute("style");
        }
    }
}
