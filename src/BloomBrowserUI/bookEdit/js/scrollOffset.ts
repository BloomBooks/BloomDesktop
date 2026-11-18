// Keeps a scrolled text box's language label (the :after in editMode.less) at the bottom of what
// the box shows. An absolutely positioned :after of a box that scrolls is placed against the box's
// content and moves with it, so while a box is scrolled its offset is kept in an inline CSS
// variable that the label's rule subtracts.

// The CSS variable that holds how far a text box is scrolled. It is part of the box's inline
// style, so a copy of the box carries it but starts unscrolled: trackScrollOffsets corrects any
// copy that is added to the page, and a copy that is used without being added to the page (the
// one that is saved, or one that is measured at once) needs removeScrollOffset.
export const kScrollTopVariable = "--bloom-scroll-top";

// Starts keeping kScrollTopVariable right on every text box in `doc`: as a box scrolls, when a
// copy of a scrolled box is added to the page, and when the text of a scrolled box changes.
export function trackScrollOffsets(doc: Document): void {
    // A scroll event does not bubble, so this listens in the capture phase.
    doc.addEventListener("scroll", recordScrollOffset, true);
    new MutationObserver(onPageChanged).observe(doc.body, {
        childList: true,
        characterData: true,
        subtree: true,
    });
}

// Keeps the offsets right after a change to the page's elements or text.
function onPageChanged(mutations: MutationRecord[]): void {
    correctAddedScrollOffsets(mutations);
    for (const m of mutations) {
        remeasureScrolledBox(m.target);
    }
}

// The boxes that have the language label. Unlike the boxes that OverflowChecker checks, this
// leaves out the textareas of a calendar's day cells: a textarea cannot have an :after, so it has
// no label to keep in place.
const kLabelledBoxSelector = ".bloom-editable";

// Records how far the text box that scrolled is scrolled.
function recordScrollOffset(e: Event): void {
    setScrollOffset(e.target);
}

// Sets kScrollTopVariable to how far `node` is scrolled, or removes it when the box is at the top.
// Does nothing if `node` is not a text box with the language label.
function setScrollOffset(node: EventTarget | null): void {
    if (!(node instanceof HTMLElement) || !node.matches(kLabelledBoxSelector)) {
        return;
    }
    if (node.scrollTop) {
        node.style.setProperty(kScrollTopVariable, node.scrollTop + "px");
    } else {
        removeScrollOffset(node);
    }
}

// Puts every text box that is added to the page, and every one in an added element, into the
// state that recordScrollOffset would have left it in had it been there all along. A copy of a
// scrolled box carries the original's offset but starts unscrolled, so this removes it.
function correctAddedScrollOffsets(mutations: MutationRecord[]): void {
    for (const m of mutations) {
        for (const node of Array.from(m.addedNodes)) {
            setScrollOffset(node);
            if (node instanceof HTMLElement) {
                node.querySelectorAll(kLabelledBoxSelector).forEach(
                    setScrollOffset,
                );
            }
        }
    }
}

// Measures again how far the scrolled text box holding `node` is scrolled, after its text changed.
// The label of a scrolled box sits as far below the box's text as the box is scrolled, so it keeps
// the box able to scroll that far even when the text no longer needs to: after Undo or deleting
// made the text fit, the box would stay scrolled with its first lines out of sight. Without its
// offset, the label is back inside the text, the browser scrolls the box back as far as the text
// allows, and the offset is set again from there. A box that is not scrolled carries no offset
// and is left alone, so typing in it costs nothing here.
function remeasureScrolledBox(node: Node): void {
    const element = node instanceof Element ? node : node.parentElement;
    const box = element?.closest<HTMLElement>(kLabelledBoxSelector);
    if (!box || !box.style.getPropertyValue(kScrollTopVariable)) {
        return;
    }
    removeScrollOffset(box);
    // Reading scrollTop lays the box out again without the offset.
    setScrollOffset(box);
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
    root.querySelectorAll<HTMLElement>(
        `[style*="${kScrollTopVariable}"]`,
    ).forEach(removeScrollOffset);
}
