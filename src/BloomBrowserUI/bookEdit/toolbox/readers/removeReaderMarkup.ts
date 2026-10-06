// Take the decodable/leveled reader tools' editing markup off a page: the live one when the tool
// is detached, or the clone we are about to save.
//
// The only such markup is the class marking a page as having more text than the level allows. The
// word- and sentence-level highlighting uses the CSS Custom Highlight API, which does not touch the
// DOM, and the hover tip is bloom-ui, which the save discards.
//
// removeSynphonyMarkup() is not usable here: it reaches into the live page frame by id, so it
// cannot clean a clone. It still unwraps the per-sentence/word spans that older versions wrote.

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
