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

import { postJson } from "../../utils/bloomApi";
import {
    getImageUrlFromImageContainer,
    GetRawImageUrl,
} from "../js/bloomImages";
import { changeImageByElement } from "../js/bloomEditing";
import { theOneCanvasElementManager } from "../js/canvasElementManager/CanvasElementManager";
import { matchReplacementsToElements } from "./aiEditorSlotMatching";
import {
    fileNameOf,
    IAiImageEditorApplyOutcome,
    IAiImageEditorCommitResult,
    isCurrentPageSwap,
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
    const imageFileName = fileNameOf(clickedUrl);
    postJson("aiImageEditor/saveThenLaunch", {
        imageFileName,
        sameNameOrdinal: sameNameOrdinalOnPage(
            imgContainer ?? img,
            imageFileName,
        ),
    });
}

// The classes C# refuses to offer the AI image editor (IsUserChangeableImageElement in
// AiImageEditorApi.cs). An empty one of those shows placeHolder.png like any other empty slot,
// so the count below has to skip them or it would run ahead of the list C# sent.
const kNotUserChangeableClasses = ["branding", "licenseImage", "bloom-qrcode"];

// Counts the slots BEFORE `clicked` on its page that show the same file name, so the overlay
// can tell two same-named slots apart (BL-16744). Every empty slot shows placeHolder.png, so
// on a page with two of them the file name alone made the overlay pick the first one, and the
// image the user made for the second slot landed in the first.
//
// Counting only the same-named slots is what keeps this in step with the list C# sent: a
// picture C# left out for having a format the editor cannot open carries its own file name, so
// it cannot shift the count. The two exclusions below cover the cases that would: a slot C#
// refuses on class alone, and the controls Bloom injects into the live page, neither of which
// is in that list.
function sameNameOrdinalOnPage(
    clicked: HTMLElement | undefined,
    imageFileName: string,
): number {
    if (!clicked || !imageFileName) return 0;
    const pageRoot = clicked.closest(".bloom-page") ?? document;
    const sameName = Array.from(
        pageRoot.querySelectorAll('img, [style*="background-image"]'),
    )
        // A container that carries the background image AND holds an <img> matches twice;
        // keep the inner one only, so each slot counts once.
        .filter((el) => el.tagName === "IMG" || !el.querySelector("img"))
        .filter(
            (el) =>
                !kNotUserChangeableClasses.some((name) =>
                    el.classList.contains(name),
                ),
        )
        // Bloom's own injected controls live in the live page only, never in the saved book
        // C# read, so they must not count either.
        .filter((el) => !el.closest(".bloom-ui"))
        .filter(
            (el) =>
                fileNameOf(GetRawImageUrl(el as HTMLElement)) === imageFileName,
        );
    const index = sameName.findIndex(
        (el) => el === clicked || el.contains(clicked) || clicked.contains(el),
    );
    return index < 0 ? 0 : index;
}

function asMessage(e: unknown): string {
    return e instanceof Error ? e.message : String(e);
}

// The elements this page's live DOM has already had an AI replacement applied to. A retry
// after a partial failure sends the same slots again, but C# reads each slot's oldSrc from
// the SAVED page, which does not have our unsaved swap yet — so for exactly these elements
// the filename check below is expected to fail, and the ordinal must be trusted anyway.
// A WeakSet keyed on the elements themselves: a page reload discards both the elements and,
// with them, the entries.
const elementsAlreadySwappedByAi = new WeakSet<HTMLElement>();

// Applies the replacements C# flagged as being on the currently-edited page. It cannot
// change that page itself (this live browser owns it), so it returns oldSrc/newSrc and we
// use Bloom's changeImageByElement() here. Returns how many swaps landed and how many were
// asked for, so the caller can report a partial failure honestly.
//
// Each swap registers an image undo, like a pasted image or a picture chosen from the
// gallery, so Ctrl+Z puts the old image back. That works because nothing here saves the
// page: the save's reload would throw the live undo stack away (the same reasoning as
// BL-16330 for ordinary image changes). The page is saved by the normal mechanisms when the
// user moves on, and every editor launch saves first (see HandleSaveThenLaunch), so later
// sessions still read a fresh book DOM.
//
// Called from the top window, so it must not assume anything about who is calling: `results`
// has crossed a frame boundary but is plain data, and everything it touches is this
// document.
export function applyAiImageEditorReplacements(
    results?: IAiImageEditorCommitResult[],
): IAiImageEditorApplyOutcome {
    const toApply = (results ?? []).filter(isCurrentPageSwap);
    if (toApply.length === 0) return { applied: 0, expected: 0 };
    const pageRoot =
        (document.querySelector(".bloom-page") as HTMLElement) || document;
    // Look up the page's image-bearing elements once, not per replacement. The selector
    // mirrors C#'s SelectChildImgAndBackgroundImageElements, and the .bloom-ui filter
    // removes the controls Bloom injects into the live page only, so each candidate's
    // index is its ordinal among the saved page's holders — the "{pageId}:{ordinal}" the
    // replacement carries.
    const candidates = Array.from(
        pageRoot.querySelectorAll('img, [style*="background-image"]'),
    ).filter((el) => !el.closest(".bloom-ui"));
    // A page can have several slots sharing the same source (every empty slot shows
    // placeHolder.png), so matchReplacementsToElements takes each replacement's slot by
    // its ordinal, checks it by filename, and consumes each matched element once. We
    // match by filename, not full src, so a cache-busting query string or path prefix on
    // the live element doesn't cause a silent miss. oldSrc arrives from C# already
    // decoded; the live srcs are encoded, so fileNameOf normalizes both sides.
    const pairs = matchReplacementsToElements(
        toApply,
        (r) => parseInt((r.incomingId ?? "").split(":").pop() ?? "", 10) || 0,
        (r) => fileNameOf(r.oldSrc, false),
        candidates as HTMLElement[],
        (el) => fileNameOf(GetRawImageUrl(el)),
        (el) => elementsAlreadySwappedByAi.has(el),
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
                // Register an image undo for each swap, like any other image change
                // (see the header comment).
                undoable: "true",
            });
            elementsAlreadySwappedByAi.add(target);
            // Make the swapped slot the active element. canUndoImageOperation only
            // offers the undo while an image container is active, and after the launch
            // saved and reloaded this page nothing is — so without this, Ctrl+Z right
            // after the editor closes would do nothing until the user clicked the image.
            theOneCanvasElementManager.setActiveElementToClosest(target);
            applied++;
        });
    } catch (e) {
        // Report rather than throw: the swaps that did land are in the live DOM, and only
        // the caller can save them, so it has to be told the count even on a failure.
        return { applied, expected: toApply.length, error: asMessage(e) };
    }
    return { applied, expected: toApply.length };
}
