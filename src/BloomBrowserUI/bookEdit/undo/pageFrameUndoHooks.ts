// Keeping the one undo stack informed about the page frame's lifetime (BL-6681).
//
// The stack lives in the workspace frame and outlives the page frame, so it has to be told when the
// page frame is replaced: page-scoped entries describe elements that are about to be rebuilt and
// mean nothing afterwards. Both hooks are called from workspaceRoot.switchContentPage, which is the
// one route C# uses to navigate the page frame (EditingView.cs) -- whether to a different page or
// to a rebuilt copy of the same one (leaving Change Layout mode, importing a video, changing the
// topic). That is why "navigating" clears rather than waiting to see whether the id changes.

import { getBloomPageElement } from "../../utils/shared";
import { theOneUndoStack, UndoStack } from "./UndoStack";

/**
 * The id of the page currently loaded in the page frame, or undefined if there is none (nothing
 * loaded yet, or about:blank).
 *
 * A page's identity is the `id` attribute of its `.bloom-page` element -- the same thing the page
 * frame itself reports to C# (editablePage.ts, getPageId). It is NOT `data-page-id`: nothing in
 * Bloom sets that attribute (only ImageUndoManagerSpec does), so the check in
 * `ImageUndoManager.clearImageOperationUndoOnPageChange` that reads it is comparing undefined with
 * undefined and never fires. That manager gets away with it because it lives in the page frame and
 * dies with the page; this stack does not, so it has to get this right.
 */
export function getCurrentPageIdFromPageFrame(): string | undefined {
    return getBloomPageElement()?.id || undefined;
}

/**
 * The page frame is about to navigate. Everything scoped to the page it is showing is now stale,
 * whether or not the next page has the same id.
 */
export function pageFrameNavigating(stack: UndoStack = theOneUndoStack): void {
    stack.clearPageScopedEntries();
}

/**
 * The page frame has loaded. Records which page entries are now being made against.
 *
 * Called from switchContentPage's load handler, and also from a dedicated once-only load listener
 * there, because that handler can be run early by a 1500 ms fallback (reading the page that is
 * still showing) and then unregistered — without the second listener the id would stay stale for
 * the rest of the page. Idempotent for an unchanged id, so running twice is harmless.
 *
 * Note the id recorded here is only used to notice page CHANGES (`setCurrentPageId` discards
 * entries scoped to other pages). Entries carry the page id their pusher gave them, and every
 * navigation clears page-scoped entries anyway (`pageFrameNavigating`), so a briefly stale id here
 * cannot mis-stamp or wrongly keep an entry.
 */
export function pageFrameLoaded(stack: UndoStack = theOneUndoStack): void {
    stack.setCurrentPageId(getCurrentPageIdFromPageFrame());
}
