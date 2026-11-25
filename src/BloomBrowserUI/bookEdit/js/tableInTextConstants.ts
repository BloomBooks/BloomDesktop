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
