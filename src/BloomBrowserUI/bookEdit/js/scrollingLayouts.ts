// The handful of page-size layouts meant to be viewed on a screen/device. When their content
// is too tall they show a scrollbar rather than reporting an overflow the way printed page sizes
// do. Keeping the list in one place lets the overflow checker (which works from a page element's
// classes) and the page-thumbnail list (which works from a layout-name string) agree on exactly
// which layouts these are. See BL-11949.
export const kScrollingLayouts = [
    "Device16x9Portrait",
    "Device16x9Landscape",
    "Ebook2x3Portrait",
    "Ebook7x5Landscape",
];

// True if the named page-size layout scrolls instead of reporting overflow.
export function layoutScrollsInsteadOfOverflowing(layoutName: string): boolean {
    return kScrollingLayouts.indexOf(layoutName) > -1;
}

// True if the given page element's layout scrolls instead of reporting overflow.
export function pageScrollsInsteadOfOverflowing(page: HTMLElement): boolean {
    return kScrollingLayouts.some((layout) => page.classList.contains(layout));
}

// The CSS variable that holds how far a text box is scrolled, set inline on the box while it is
// scrolled (see respondToBoxScrolling in overflowIndicators.ts), which keeps its language label
// (editMode.less) at the bottom of what the box shows. A copy of the box starts unscrolled, so
// whatever copies a box removes it.
export const kScrollTopVariable = "--bloom-scroll-top";

// Removes kScrollTopVariable from every text box in `root`, a copy of the page about to be
// saved, so that it never reaches the book.
export function removeScrollOffsets(root: HTMLElement): void {
    for (const box of Array.from(
        root.querySelectorAll<HTMLElement>(`[style*="${kScrollTopVariable}"]`),
    )) {
        box.style.removeProperty(kScrollTopVariable);
        if (!box.getAttribute("style")) {
            // Don't leave an empty style attribute behind in the saved HTML.
            box.removeAttribute("style");
        }
    }
}
