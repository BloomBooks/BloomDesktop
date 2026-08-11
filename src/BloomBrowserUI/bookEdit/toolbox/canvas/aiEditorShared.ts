// Types and helpers shared by the two halves of the AI Image Editor integration: the
// overlay/session, which runs in the TOP window (aiEditorOverlay.ts), and the live-page
// work, which must run in the PAGE frame (aiEditorPageCommands.ts). Deliberately pure —
// no DOM, no api calls — so it is safe in every bundle either half lands in.

// The image the user right-clicked, as the page frame reports it to C# and as C# hands it
// back to the overlay once the page has been saved. Plain data, because the save reloads
// the page frame: a live element reference would not survive the round trip. The page id
// comes from C# (it knows which page it saved), so the overlay needs no DOM read to
// match the click against the book image list.
export interface IAiImageEditorTarget {
    pageId: string;
    imageFileName: string;
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

// Pull the file name off an image url. `encoded` says whether the url is percent-encoded:
// live DOM srcs and host-served URLs are, but oldSrc in commit results arrives from C#
// already decoded (PathOnly.NotEncoded) — decoding it again corrupts (or throws on)
// filenames containing a literal '%'. On a failed decode fall back to the raw name rather
// than "", so an oddly-encoded src degrades to a possible mismatch instead of matching
// nothing ever.
export function fileNameOf(
    url?: string | null,
    encoded: boolean = true,
): string {
    const raw = (url ?? "").split("?")[0].split("/").pop() ?? "";
    if (!encoded) return raw;
    try {
        return decodeURIComponent(raw);
    } catch {
        return raw;
    }
}
