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
import { kImageContainerClass } from "../js/bloomImages";
import { changeImageByElement } from "../js/bloomEditing";
import { theOneCanvasElementManager } from "../js/canvasElementManager/CanvasElementManager";
import {
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
    postJson("aiImageEditor/saveThenLaunch", {
        slotIndex: slotIndexOnPage(imgContainer ?? img),
    });
}

// Numbers this page's image slots the way C# does (SelectImageSlotsOnPage in
// AiImageEditorApi.cs): its image containers, in document order. An image container is
// exactly what a user may replace, so the branding, license and QR-code images, which live
// outside any container, are not slots at all.
//
// The index IS the slot's identity — it is the "{pageId}:{ordinal}" ordinal C# builds — so the
// two lists have to hold the same containers. Bloom injects controls into the live page that
// no saved book has, and the save strips them (Cleanup in bloomEditing.ts), so those are the
// one thing to leave out here.
function slotIndexOnPage(clicked: HTMLElement | undefined): number {
    if (!clicked) return 0;
    const pageRoot = clicked.closest(".bloom-page") ?? document;
    const slots = Array.from(
        pageRoot.querySelectorAll("." + kImageContainerClass),
    ).filter((el) => !el.closest(".bloom-ui"));
    const index = slots.findIndex(
        (el) => el === clicked || el.contains(clicked) || clicked.contains(el),
    );
    return index < 0 ? 0 : index;
}

// The element of a slot that carries the picture: the container's own img, or the container
// itself when it wears the picture as a background image. Mirrors GetImageElementOfSlot in
// AiImageEditorApi.cs. It matters here because changeImageInfo sets a background image on
// anything that is not an <img>, so handing it a container that holds an img would leave the
// img untouched and paint the new picture behind it.
function imageElementOfSlot(slot: HTMLElement): HTMLElement {
    const img = Array.from(slot.children).find((c) => c.tagName === "IMG");
    return (img as HTMLElement) ?? slot;
}

function asMessage(e: unknown): string {
    return e instanceof Error ? e.message : String(e);
}

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
    // The page's slots, numbered as slotIndexOnPage numbers them and as C# numbers them
    // (SelectImageSlotsOnPage): the image containers, less the controls Bloom injects into
    // the live page. The ordinal in a replacement's "{pageId}:{ordinal}" is an index into
    // this list, and that index is the whole of a slot's identity — nothing here compares
    // file names, because two slots can honestly show the same file (every empty slot shows
    // placeHolder.png) and a slot we already swapped no longer shows what C# read.
    const slots = Array.from(
        pageRoot.querySelectorAll("." + kImageContainerClass),
    ).filter((el) => !el.closest(".bloom-ui")) as HTMLElement[];
    // Count as we go rather than at the end: if a swap throws, the ones already made are in
    // the live DOM and the caller still has to know to save them. A replacement whose slot
    // this page does not have is left out, which the caller sees as applied < expected.
    let applied = 0;
    try {
        toApply.forEach((r) => {
            const slot =
                slots[
                    parseInt((r.incomingId ?? "").split(":").pop() ?? "", 10) ||
                        0
                ];
            if (!slot) return;
            const target = imageElementOfSlot(slot);
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
