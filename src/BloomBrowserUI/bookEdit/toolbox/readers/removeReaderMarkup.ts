// Take the decodable/leveled reader tools' editing markup off a page — either the live one, when
// the tool is being detached, or the clone we are about to save.
//
// The reader tools mark up the page while you edit: a class on the page div when there is more
// text on it than the level allows, and (from older versions of the markup code) spans wrapping
// each sentence/word/grapheme. None of that belongs in the user's book.
//
// The tools' own removeSynphonyMarkup() cannot do this job, because it reaches into the live page
// frame by id rather than working on an element it is given, so it can only ever clean the page
// the user is looking at. That was fine when saving destroyed the live page anyway; now that we
// save from a clone (BL-13502), the cleanup has to be element-scoped, which is what this is.
//
// Note that the reader *highlights* deliberately are not handled here: they are drawn with the CSS
// Custom Highlight API rather than by changing the DOM, and the hover tip is `bloom-ui`, which the
// C# save pipeline discards. So neither can reach the file.

const kTooMuchStuffOnPageClass = "page-too-many-words-or-sentences";

export function removeReaderMarkup(pageOrClone: HTMLElement): void {
    // The class lives on the .bloom-page div, which may be the element we were given or inside it.
    if (pageOrClone.classList.contains(kTooMuchStuffOnPageClass))
        pageOrClone.classList.remove(kTooMuchStuffOnPageClass);
    for (const marked of Array.from(
        pageOrClone.getElementsByClassName(kTooMuchStuffOnPageClass),
    ))
        marked.classList.remove(kTooMuchStuffOnPageClass);

    for (const segment of Array.from(
        pageOrClone.querySelectorAll("span[data-segment]"),
    )) {
        // Unwrap: put the span's children where the span was, then drop it. (An empty one just
        // goes.) Matches what removeSynphonyMarkup does to the live page.
        const parent = segment.parentNode;
        if (!parent) continue;
        while (segment.firstChild)
            parent.insertBefore(segment.firstChild, segment);
        parent.removeChild(segment);
    }
}
