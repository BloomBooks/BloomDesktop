// Which books a folio's table of contents list still names (see folioToc.ts).

/**
 * Compare a list's books with the books now in the collection. Returns the ids to keep, in order,
 * the titles to store for them (their current titles), and the last known titles of the books
 * that are no longer in the collection (or their ids, for a book whose title was never stored).
 */
export function compareFolioListWithCollection(
    ids: string[],
    storedTitles: Record<string, string>,
    collectionTitles: Map<string, string>,
): { ids: string[]; titles: Record<string, string>; removed: string[] } {
    const kept: string[] = [];
    const titles: Record<string, string> = {};
    const removed: string[] = [];
    for (const id of ids) {
        const title = collectionTitles.get(id);
        if (title === undefined) {
            removed.push(storedTitles[id] ?? id);
            continue;
        }
        kept.push(id);
        titles[id] = title;
    }
    return { ids: kept, titles, removed };
}
