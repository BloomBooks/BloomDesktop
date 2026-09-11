import { kSelectorForPotentialNiceScrollElements } from "bloom-player";

// The classes niceScroll gives the elements it inserts. Each rail contains a cursor (its word for
// the thumb); we list both so a stray one can't survive.
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
 * The point of this existing at all — bloom-player already has cleanupNiceScroll() — is that this
 * works on any root, including a DETACHED CLONE of the page. bloom-player's version can only work
 * on the live page, because it does the job by asking each live niceScroll instance to remove
 * itself. Doing that on every save meant tearing the scroll bars off the page the user was looking
 * at and building them again (see getBodyContentForSavePage in bloomEditing.ts).
 *
 * There are three kinds of leftovers:
 *
 * 1. The elements niceScroll inserts: a .nicescroll-rails div (vertical, plus a horizontal one if
 *    needed), each containing a .nicescroll-cursors div. It appends them to the nearest positioned
 *    or scrollable ancestor and falls back to the body. Bloom pages do contain absolutely
 *    positioned ancestors (origami split-pane components, image-description groups), so they can
 *    land inside the .bloom-page div; when there is no such ancestor they go on the body instead.
 *    We are given the whole body, so we catch them either way.
 *
 * 2. Classes that addScrollbarsToPage() changed, because niceScroll does not work with the
 *    display:flex our vertical alignment implies: it moves bloom-vertical-align-center /
 *    bloom-vertical-align-bottom aside to a "-removed" marker on the translationGroup, and adds
 *    scrolling-bubble to a canvas element's editable. This is the part that matters most —
 *    saving a page in that state would silently lose the user's vertical alignment choice.
 *
 * 3. Inline styles niceScroll sets on the box it scrolls: overflow-x and overflow-y (hidden),
 *    outline (none, on webkit), and a pixel width (part of a Chrome scrollbar workaround, which it
 *    tries but does not always manage to undo — BL-14052). Those three are exactly what
 *    bloom-player's cleanup clears after asking niceScroll to remove itself, i.e. the ones
 *    niceScroll sets without recording so that it can restore them.
 *    (It can also set position:relative on the scrolled element, but only when it was created with
 *    a wrapper — the two-argument niceScroll() form — which bloom-player does not use, so that
 *    case cannot arise here.)
 */
export function removeNiceScrollArtifacts(root: HTMLElement): void {
    for (const inserted of Array.from(
        root.querySelectorAll(kNiceScrollInsertedElementSelector),
    )) {
        inserted.remove();
    }

    for (const alignClass of kVerticalAlignClassesRemovedForNiceScroll) {
        const removedMarker = alignClass + "-removed";
        // getElementsByClassName is live, and we are about to remove the very class it selects on,
        // so take a copy first.
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
        // An inline overflow-y is niceScroll's fingerprint: it is the first thing it sets on a box
        // it is going to scroll, and nothing in Bloom sets one. Checking for it means we can't
        // blank an inline width that really was the author's on a box niceScroll never touched.
        // (bloom-player's cleanup clears all three unconditionally; it can afford to, because it
        // only reaches boxes that had a live niceScroll instance.)
        if (!scrollBox.style.overflowY) {
            continue;
        }
        // Naming the longhands explicitly rather than clearing the "overflow" shorthand: whether
        // clearing a shorthand takes its longhands with it varies between CSSOM implementations
        // (jsdom, where our tests run, does not do it).
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
