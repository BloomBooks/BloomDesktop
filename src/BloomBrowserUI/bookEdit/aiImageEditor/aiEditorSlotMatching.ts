// Pure slot-matching helper for applying AI-editor replacements to the currently-open page.
//
// When the AI commit returns replacements for the page the user is looking at, the front-end
// has to pair each replacement with the right live image element. The ordinal in each
// replacement's "{pageId}:{n}" id counts the saved page's image holders in document order,
// which is the live candidates' order too (the caller strips the live-only elements), so the
// candidate AT that index is the slot the user chose — a page can hold several slots showing
// the same filename (every empty slot shows placeHolder.png), and a lone replacement for the
// seventh of them must not land on the first (BL-16744).
//
// The filename (from the replacement's oldSrc) is the safety check on that index: the live
// page can grow an image-bearing element the saved page lacks, which would shift every index
// after it. A candidate whose filename is not the one the replacement expects is refused, and
// the replacement falls back to the first unused same-filename candidate — the pre-BL-16744
// behavior, wrong only among same-named slots and safe everywhere else. Filename, not full
// src, because a cache-busting query string or path prefix on the live element would defeat a
// full-src compare. Each candidate is consumed at most once, so distinct replacements land on
// distinct elements.
//
// This is factored out of aiEditorPageCommands.ts's apply step so the pairing logic can be
// unit-tested without a DOM or the changeImage side effects; the caller supplies the
// ordinal/filename accessors and performs the actual image swap on the returned pairs.

export interface IReplacementMatch<TReplacement, TElement> {
    replacement: TReplacement;
    element: TElement;
}

/**
 * Pairs each replacement with the candidate at its slot ordinal, falling back to the first
 * unused same-filename candidate when that index is out of range, already used, or shows a
 * different filename (see the header). Applies in ascending ordinal order and uses each
 * candidate at most once. A replacement with no filename match (given the still-unused
 * candidates) is skipped and simply omitted from the result.
 *
 * @param replacements the current-page replacements to place
 * @param ordinalOf extracts a replacement's slot ordinal — the index of its slot among the
 *          saved page's image holders, which the candidates must mirror
 * @param wantedFilenameOf the filename a replacement wants to land on (from its oldSrc)
 * @param candidates the live page's image-bearing elements, in document order, with the
 *          live-only elements (e.g. Bloom's injected controls) already removed so indexes
 *          line up with the saved page's holders
 * @param candidateFilenameOf the filename currently shown by a candidate element
 * @returns one {replacement, element} pair per successfully matched replacement, in the order
 *          they were applied (ascending ordinal)
 */
export function matchReplacementsToElements<TReplacement, TElement>(
    replacements: TReplacement[],
    ordinalOf: (replacement: TReplacement) => number,
    wantedFilenameOf: (replacement: TReplacement) => string,
    candidates: TElement[],
    candidateFilenameOf: (element: TElement) => string,
): Array<IReplacementMatch<TReplacement, TElement>> {
    const used = new Set<TElement>();
    const matches: Array<IReplacementMatch<TReplacement, TElement>> = [];
    [...replacements]
        .sort((a, b) => ordinalOf(a) - ordinalOf(b))
        .forEach((replacement) => {
            const wanted = wantedFilenameOf(replacement);
            const atOrdinal = candidates[ordinalOf(replacement)];
            const element =
                atOrdinal !== undefined &&
                !used.has(atOrdinal) &&
                candidateFilenameOf(atOrdinal) === wanted
                    ? atOrdinal
                    : candidates.find(
                          (candidate) =>
                              !used.has(candidate) &&
                              candidateFilenameOf(candidate) === wanted,
                      );
            if (element === undefined) {
                return;
            }
            used.add(element);
            matches.push({ replacement, element });
        });
    return matches;
}
