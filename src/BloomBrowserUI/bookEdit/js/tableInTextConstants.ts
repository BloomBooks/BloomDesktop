// Names and small helpers for tables inserted into the text of a text box (see
// docs/insert-table/README.md) that code which cannot import the table modules without an import
// cycle, or without loading the table library, also needs, such as editableDivUtils.ts,
// bloomEditing.ts and OverflowChecker.ts.

// The class of a bloom-table that sits in the text of a bloom-editable, between its paragraphs,
// rather than filling a page-layout section or a canvas element.
export const kTableInTextClass = "bloom-tableInText";

/**
 * The empty paragraph at the end of `box` that comes right after a table in text, if there is one
 * and nothing has been typed in it. ensureParagraphOnEachSide in tablesInText.ts adds it so that
 * the user has somewhere to type below a table that ends the text.
 */
export function unusedParagraphBelowLastTable(
    box: HTMLElement,
): HTMLElement | undefined {
    const paragraphs = box.querySelectorAll<HTMLElement>(":scope > p");
    const last = paragraphs[paragraphs.length - 1];
    if (!last?.previousElementSibling?.classList.contains(kTableInTextClass)) {
        return undefined;
    }
    // A <br> is what keeps an empty paragraph one line high; a span may be left empty by
    // the editor. Anything else, such as a picture, is content.
    const isEmpty =
        (last.textContent ?? "").trim() === "" &&
        !last.querySelector(":not(br, span)");
    return isEmpty ? last : undefined;
}

/** The element `node` is, or for a text node the element it is in. */
export function elementOf(node: Node): Element | null {
    return node.nodeType === Node.ELEMENT_NODE
        ? (node as Element)
        : node.parentElement;
}

/** The tables in `box`'s own text, in order. */
export function tablesInTextOf(box: HTMLElement): HTMLElement[] {
    return Array.from(
        box.querySelectorAll<HTMLElement>(`:scope > .${kTableInTextClass}`),
    );
}

/**
 * After something has rewritten `box`'s content from HTML, put `liveTables` (from tablesInTextOf
 * before the rewrite) back in place of the copies the HTML made. Tables have no id, so they are
 * matched by their order in the box. A box that had no live tables, such as an empty scratch div
 * that Talking Book fills with CKEditor's data, has nothing to keep and takes the HTML as it is.
 */
export function putBackLiveTables(
    box: HTMLElement,
    liveTables: HTMLElement[],
): void {
    if (liveTables.length === 0) return;
    const copies = tablesInTextOf(box);
    if (copies.length !== liveTables.length)
        throw new Error(
            `The new content of the text box has ${copies.length} tables, but it had ${liveTables.length}`,
        );
    copies.forEach((copy, index) => {
        if (copy !== liveTables[index]) copy.replaceWith(liveTables[index]);
    });
}
