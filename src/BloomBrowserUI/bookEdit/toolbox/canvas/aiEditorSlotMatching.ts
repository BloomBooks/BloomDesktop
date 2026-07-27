// Pure slot-matching helper for applying AI-editor replacements to the currently-open page.
//
// When the AI commit returns replacements for the page the user is looking at, the front-end
// has to pair each replacement with the right live image element. Matching is by filename
// (a cache-busting query string or path prefix on the live element would defeat a full-src
// compare), but a single page can have several image slots that share the same filename —
// e.g. two empty placeholders, or the same photo used twice. Filename alone can't tell those
// apart, so we (a) apply in slot order — the ordinal in each replacement's "{pageId}:{n}" id
// counts the saved page's image holders in document order, which is the live candidates'
// order too — and (b) consume each element at most once, so distinct replacements land on
// distinct elements instead of all collapsing onto the first same-filename match.
//
// This is factored out of canvasControlRegistry.ts's editWithAi command so the pairing logic
// can be unit-tested without a DOM or the changeImage side effects; the caller supplies the
// ordinal/filename accessors and performs the actual image swap on the returned pairs.

export interface IReplacementMatch<TReplacement, TElement> {
    replacement: TReplacement;
    element: TElement;
}

/**
 * Pairs replacements to candidate elements by filename, in ascending ordinal order, using each
 * candidate at most once. A replacement with no filename match (given the still-unused
 * candidates) is skipped and simply omitted from the result.
 *
 * @param replacements the current-page replacements to place
 * @param ordinalOf extracts a replacement's slot ordinal (used only to order placement)
 * @param wantedFilenameOf the filename a replacement wants to land on (from its oldSrc)
 * @param candidates the live page's image-bearing elements, in document order
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
            const element = candidates.find(
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
