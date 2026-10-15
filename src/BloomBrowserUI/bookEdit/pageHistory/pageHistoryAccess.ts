// Access to the page history from any frame of the Edit tab: the page frame, the toolbox, the top
// bar, or a dialog. The history lives in the page frame (one per page load), so these helpers reach
// it through the page frame's bundle exports rather than importing pageHistory.ts, which would give
// each frame its own copy of that module with no history in it. Each helper does nothing when there
// is no page frame or no history, which is the case outside the Edit tab, before
// startPageHistory is called, and in unit tests.

import { getEditablePageBundleExports } from "../js/workspaceFrames";
import type { PageHistory } from "./pageHistoryTypes";

/**
 * The history of the page open in the Edit tab, or undefined when there is no page frame, no
 * history has been started, or the current one has been disposed.
 */
export function currentPageHistory(): PageHistory | undefined {
    const pageFrameExports = getEditablePageBundleExports();
    if (!pageFrameExports) return undefined;
    let history: PageHistory;
    try {
        history = pageFrameExports.pageHistory();
    } catch {
        // pageHistory() throws when startPageHistory has not been called, and pageHistory.ts
        // offers no way to ask whether there is one without throwing.
        return undefined;
    }
    // A disposed history throws on every call.
    if (history.isDisposed) return undefined;
    return history;
}

/**
 * Run fn in the current page history's doOutsideHistory, so that the changes it makes are not
 * recorded; with no history, just run fn. Only for values that Bloom recomputes from other content.
 */
export function doOutsideHistoryIfAny<T>(fn: () => T): T {
    const history = currentPageHistory();
    return history ? history.doOutsideHistory(fn) : fn();
}

/** End the current page history (drop every undo and redo step), if there is one. */
export function clearUndoAndRedoIfAny(): void {
    currentPageHistory()?.clearUndoAndRedo();
}
