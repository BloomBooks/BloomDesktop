// Take the decodable/leveled reader tools' editing markup off a page — either the live one, when
// the tool is being detached, or the clone we are about to save.
//
// There is exactly one thing to remove: the class the tools put on the .bloom-page div to mark a
// page as having more text on it than the level allows. It is an editing aid and must not be
// stored in the user's book.
//
// Nothing has to be done inside the text itself. The tools' word- and sentence-level highlighting
// is drawn with the CSS Custom Highlight API, which paints ranges without touching the DOM, and
// the hover tip is `bloom-ui`, which the C# save pipeline discards. (Older versions of the markup
// code did wrap each sentence/word/grapheme in a span, which is why removeSynphonyMarkup() still
// unwraps those; the only place that still produces them is the Reader Setup dialog's own word
// list, which is never part of a book.)
//
// removeSynphonyMarkup() cannot do this job in any case, because it reaches into the live page
// frame by id rather than working on an element it is given, so it can only ever clean the page
// the user is looking at. That was fine when saving destroyed the live page anyway; now that we
// save from a clone (BL-13502), the cleanup has to be element-scoped, which is what this is.

const kTooMuchStuffOnPageClass = "page-too-many-words-or-sentences";

export function removeReaderMarkup(pageOrClone: HTMLElement): void {
    // The class lives on the .bloom-page div, which may be the element we were given (when a tool
    // is detached from the live page) or inside it (when we are cleaning a clone of the body).
    if (pageOrClone.classList.contains(kTooMuchStuffOnPageClass))
        pageOrClone.classList.remove(kTooMuchStuffOnPageClass);
    for (const marked of Array.from(
        pageOrClone.getElementsByClassName(kTooMuchStuffOnPageClass),
    ))
        marked.classList.remove(kTooMuchStuffOnPageClass);
}
