// The PAGE-FRAME half of the AI Image Editor integration — the only two things that have
// to run where the live page lives. The overlay and the whole conversation with the AI
// image editor are in the top window (aiEditorOverlay.ts); read that file's header for the
// full flow, and AiImageEditorApi.cs for the C# side.
//
//   launchAiImageEditor            the "Edit with AI…" menu command: report the clicked
//                                  image to C# and ask it to save the page.
//   applyAiImageEditorReplacements the commit's current-page swaps, applied to the live DOM.
//
// Both are reached from elsewhere: the first from canvasControlRegistry (this frame), the
// second from the overlay in the top window, via
// getEditablePageBundleExports().applyAiImageEditorReplacements().

import { postJson } from "../../../utils/bloomApi";
import {
    getImageUrlFromImageContainer,
    GetRawImageUrl,
} from "../../js/bloomImages";
import { changeImageByElement } from "../../js/bloomEditing";
import { matchReplacementsToElements } from "./aiEditorSlotMatching";
import {
    fileNameOf,
    IAiImageEditorApplyOutcome,
    IAiImageEditorCommitResult,
} from "./aiEditorShared";

// Starts "Edit with AI…" for the given image. `img` is the right-clicked image and
// `imgContainer` its image container (if any).
//
// This does not open the editor. Everything C# tells the editor about the book — the
// whole-book image list, and on commit each slot's current src — is read from the SAVED
// book DOM, but an image the user has just added lives only in this live page, because
// changeImage/changeImageByElement deliberately do not save (BL-16330). Launching against
// that stale DOM opened the editor with an empty "Image to Edit" slot (BL-16682). So we
// hand the clicked image to C#, which saves the page and then opens the overlay itself
// (AiImageEditorApi.HandleSaveThenLaunch). Only the file name travels: saving reloads this
// frame, so a live element reference would not survive.
export function launchAiImageEditor(
    img: HTMLImageElement,
    imgContainer: HTMLElement | undefined,
): void {
    const clickedUrl = imgContainer
        ? getImageUrlFromImageContainer(imgContainer)
        : img?.getAttribute("src");
    postJson("aiImageEditor/saveThenLaunch", {
        imageFileName: fileNameOf(clickedUrl),
    });
}

function asMessage(e: unknown): string {
    return e instanceof Error ? e.message : String(e);
}

// Applies the replacements C# flagged as being on the currently-edited page. It cannot
// change that page itself (this live browser owns it), so it returns oldSrc/newSrc and we
// use Bloom's changeImageByElement() here. Returns how many swaps landed and how many were
// asked for, so the caller can report a partial failure honestly.
//
// Called from the top window, so it must not assume anything about who is calling: `results`
// has crossed a frame boundary but is plain data, and everything it touches is this
// document.
export function applyAiImageEditorReplacements(
    results?: IAiImageEditorCommitResult[],
): IAiImageEditorApplyOutcome {
    const toApply = (results ?? []).filter(
        (r) => r && r.ok && r.isCurrentPage && r.newSrc && r.oldSrc,
    );
    if (toApply.length === 0) return { applied: 0, expected: 0 };
    const pageRoot =
        (document.querySelector(".bloom-page") as HTMLElement) || document;
    // Look up the page's image-bearing elements once, not per replacement.
    const candidates = Array.from(
        pageRoot.querySelectorAll('img, [style*="background-image"]'),
    );
    // A page can have several slots sharing the same source (e.g. multiple empty
    // placeholders). matchReplacementsToElements consumes each matched element once so
    // distinct replacements land on distinct elements instead of collapsing onto the first
    // match, and applies in slot (ordinal) order. We match by filename, not full src, so a
    // cache-busting query string or path prefix on the live element doesn't cause a silent
    // miss. oldSrc arrives from C# already decoded; the live srcs are encoded, so
    // fileNameOf normalizes both sides.
    const pairs = matchReplacementsToElements(
        toApply,
        (r) => parseInt((r.incomingId ?? "").split(":").pop() ?? "", 10) || 0,
        (r) => fileNameOf(r.oldSrc, false),
        candidates as HTMLElement[],
        (el) => fileNameOf(GetRawImageUrl(el)),
    );
    // Count as we go rather than from pairs.length at the end: if a swap throws, the ones
    // already made are in the live DOM and the caller still has to know to save them.
    let applied = 0;
    try {
        pairs.forEach(({ replacement: r, element: target }) => {
            changeImageByElement(target, {
                src: r.newSrc as string,
                // Take the credits from C#, which read them off the new image file.
                // Reading them off `target` instead would carry the REPLACED image's
                // credits forward, so the page would go on showing credits (and no
                // "missing information" indicator) for a new image that has none — until
                // the next book-up-to-date pass re-derived them from the file and they
                // silently vanished (BL-16603).
                creator: r.creator ?? "",
                copyright: r.copyright ?? "",
                license: r.license ?? "",
                // The AI commit applies replacements book-wide in C# (saved directly, not
                // undoable), so don't register a separate per-image undo for the
                // current-page piece.
                undoable: "false",
            });
            applied++;
        });
    } catch (e) {
        // Report rather than throw: the swaps that did land are in the live DOM, and only
        // the caller can save them, so it has to be told the count even on a failure.
        return { applied, expected: toApply.length, error: asMessage(e) };
    }
    return { applied, expected: toApply.length };
}
