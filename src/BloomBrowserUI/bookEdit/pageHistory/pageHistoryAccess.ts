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
 * recorded and keep the history as it is; with no history, just run fn. Use it for a value that
 * Bloom recomputes from other content, and for a change that is deliberately not undoable but must
 * not end the history. A later undo or redo that needs a value such a change altered is refused:
 * that stack of steps is emptied and the page is left as it is (PageHistory.doOutsideHistory).
 */
export function doOutsideHistory<T>(fn: () => T): T {
    const history = currentPageHistory();
    return history ? history.doOutsideHistory(fn) : fn();
}

/**
 * Run fn in the current page history's addToLatestTransaction, so that its changes join the
 * latest transaction without a warning; with no history, just run fn. Use it for Bloom's delayed
 * reaction to an earlier action: while the only transaction open is the stand-in of a press that
 * has changed nothing yet, the changes join the latest step, so the reaction never makes a step
 * of its own (PageHistory.addToLatestTransaction).
 */
export function addToLatestTransaction<T>(fn: () => T): T {
    const history = currentPageHistory();
    return history ? history.addToLatestTransaction(fn) : fn();
}

/**
 * Give the undo step that the open transaction will make the description descriptionL10nKey,
 * unless something has described it already, for an action that runs inside the transaction that
 * the user's click, drag or key press opened, and that has no transaction of its own. It opens
 * and closes an empty transaction inside the open one, and the first transaction nested in the
 * stand-in with the general description gives the step its description
 * (PageHistory.doTransaction). It records nothing and makes no step of its own. Does nothing when
 * no transaction is open, or when there is no history.
 *
 * Call it only from an action in the page frame: a press in the toolbox or the root window whose
 * step keeps the general description ends the history (pageHistoryInput.ts), and describing that
 * step would make it an undo step instead.
 */
export function describeOpenStep(descriptionL10nKey: string): void {
    const history = currentPageHistory();
    if (!history?.hasOpenTransaction) return;
    void history.doTransaction(descriptionL10nKey, () => {});
}

/**
 * End the current page history (drop every undo and redo step), if there is one. reason says why,
 * for Bloom's log.
 */
export function clearUndoAndRedo(reason: string): void {
    currentPageHistory()?.clearUndoAndRedo(reason);
}
