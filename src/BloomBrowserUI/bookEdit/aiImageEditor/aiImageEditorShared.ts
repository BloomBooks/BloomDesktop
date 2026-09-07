// Types and helpers shared by the two halves of the AI Image Editor integration: the
// overlay/session, which runs in the TOP window (aiImageEditorOverlay.ts), and the live-page
// work, which must run in the PAGE frame (aiImageEditorPageCommands.ts). Deliberately pure —
// no DOM, no api calls — so it is safe in every bundle either half lands in.

// The image the user right-clicked, as the page frame reports it to C# and as C# hands it
// back to the overlay once the page has been saved. Plain data, because the save reloads
// the page frame: a live element reference would not survive the round trip. The page id
// comes from C# (it knows which page it saved), so the overlay needs no DOM read to
// match the click against the book image list.
export interface IAiImageEditorTarget {
    pageId: string;
    // Which image slot of that page the user clicked, as its index among the page's image
    // containers in document order. That index is the slot's whole identity: it is the
    // ordinal in the book image's "{pageId}:{ordinal}" id, so the overlay names the clicked
    // image by building that id rather than by looking for a file name. A file name could
    // never have said which slot was clicked anyway, since every empty slot shows
    // placeHolder.png and one photo can be used twice on a page.
    slotIndex: number;
}

// One entry of aiImageEditor/commit's reply. The ones flagged isCurrentPage are the slots
// C# could not write itself, because the live browser owns that page; those carry the
// old/new src and the credits the new file's embedded metadata calls for, as C# read them
// back off that file.
export interface IAiImageEditorCommitResult {
    incomingId?: string;
    ok?: boolean;
    isCurrentPage?: boolean;
    oldSrc?: string;
    newSrc?: string;
    copyright?: string;
    creator?: string;
    license?: string;
}

// Whether this commit result is a swap the page frame has to make on the live page. Shared so
// the two halves cannot disagree: the overlay uses it to decide whether it needs the page frame
// at all, and the page frame uses it to pick the results to apply.
export function isCurrentPageSwap(
    result?: IAiImageEditorCommitResult,
): boolean {
    return !!(
        result &&
        result.ok &&
        result.isCurrentPage &&
        result.newSrc &&
        result.oldSrc
    );
}

// What the page frame reports back about the current-page swaps it was asked to make.
// `applied` counts swaps that actually landed in the live DOM; the overlay compares it
// with `expected` to tell the AI image editor the truth about a partial failure. A swap
// that throws part way through is reported as a lower `applied` plus `error` rather than
// as an exception, so the overlay always learns how much there is to save.
export interface IAiImageEditorApplyOutcome {
    applied: number;
    expected: number;
    error?: string;
}
