// Keeps a scrolled text box's language label (the :after in editMode.less) at the bottom of what
// the box shows. An absolutely positioned :after of a box that scrolls is placed against the box's
// content and moves with it, so while a box is scrolled its offset is kept in an inline CSS
// variable that the label's rule subtracts.

// The CSS variable that holds how far a text box is scrolled. It is part of the box's inline
// style, so a copy of the box carries it but starts unscrolled: trackScrollOffsets corrects any
// copy that is added to the page, and a copy that is used without being added to the page (the
// one that is saved, or one that is measured at once) needs removeScrollOffset.
export const kScrollTopVariable = "--bloom-scroll-top";

// Starts keeping kScrollTopVariable right on every text box in `doc`: as a box scrolls, and when a
// copy of a scrolled box is added to the page.
export function trackScrollOffsets(doc: Document): void {
    // A scroll event does not bubble, so this listens in the capture phase.
    doc.addEventListener("scroll", recordScrollOffset, true);
    new MutationObserver(correctAddedScrollOffsets).observe(doc.body, {
        childList: true,
        subtree: true,
    });
}

// Records how far the text box that scrolled is scrolled.
function recordScrollOffset(e: Event): void {
    if (
        e.target instanceof HTMLElement &&
        e.target.matches(".bloom-editable")
    ) {
        setScrollOffset(e.target);
    }
}

// Sets kScrollTopVariable to how far `box` is scrolled, or removes it when the box is at the top.
function setScrollOffset(box: HTMLElement): void {
    if (box.scrollTop) {
        box.style.setProperty(kScrollTopVariable, box.scrollTop + "px");
    } else {
        removeScrollOffset(box);
    }
}

// Corrects the offset of every added element that carries one, which is a copy of a scrolled box.
function correctAddedScrollOffsets(mutations: MutationRecord[]): void {
    for (const m of mutations) {
        for (const node of Array.from(m.addedNodes)) {
            if (node instanceof HTMLElement) {
                withScrollOffset(node).forEach(setScrollOffset);
            }
        }
    }
}

// Removes kScrollTopVariable from `box`, and the style attribute if that leaves it empty.
export function removeScrollOffset(box: HTMLElement): void {
    box.style.removeProperty(kScrollTopVariable);
    if (!box.getAttribute("style")) {
        // Don't leave an empty style attribute behind in the saved HTML.
        box.removeAttribute("style");
    }
}

// Removes kScrollTopVariable from every element in `root`, a copy of the page about to be saved,
// so that it never reaches the book.
export function removeScrollOffsets(root: HTMLElement): void {
    withScrollOffset(root).forEach(removeScrollOffset);
}

// `root` and the elements in it that carry kScrollTopVariable.
function withScrollOffset(root: HTMLElement): HTMLElement[] {
    const selector = `[style*="${kScrollTopVariable}"]`;
    return [
        ...(root.matches(selector) ? [root] : []),
        ...Array.from(root.querySelectorAll<HTMLElement>(selector)),
    ];
}
