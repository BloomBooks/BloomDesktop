// Take the decodable/leveled reader tools' editing markup off a page: the live one when the tool
// is detached, or the copy of it about to be saved.
//
// The only markup the tools add is the class marking a page as having more text than the level
// allows. Their word- and sentence-level highlighting uses the CSS Custom Highlight API, which does
// not touch the DOM, and the hover tip is bloom-ui, which the save discards.
//
// (Older versions of Bloom wrapped words and sentences in spans. Those are old content of the book
// rather than markup the tools add now; the tools unwrap them from the live page when they mark it
// up, through removeSynphonyMarkup in libSynphony.)

const kTooMuchStuffOnPageClass = "page-too-many-words-or-sentences";

export function removeReaderMarkup(pageOrClone: HTMLElement): void {
    // The class lives on the .bloom-page div, which is normally the element we are given; the loop
    // covers being given an element that contains it.
    if (pageOrClone.classList.contains(kTooMuchStuffOnPageClass))
        pageOrClone.classList.remove(kTooMuchStuffOnPageClass);
    for (const marked of Array.from(
        pageOrClone.getElementsByClassName(kTooMuchStuffOnPageClass),
    ))
        marked.classList.remove(kTooMuchStuffOnPageClass);
}
