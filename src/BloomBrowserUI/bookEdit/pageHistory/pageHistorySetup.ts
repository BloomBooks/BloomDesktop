// The page history of the page open in the Edit tab, set up in the page frame: started once the
// page is ready, with the user's input and the undo and redo keys attached, and ended when the page
// goes. Also the entry points that the rest of Bloom undoes and redoes through, all exported on the
// page bundle (editablePage.ts):
// - the Undo and Redo buttons: the top bar, in the root window, calls handleUndo and handleRedo;
// - their enabled state: the top bar polls canUndo and canRedo (editTopBarControls.tsx);
// - the keys: pageHistoryKeys.ts runs handleUndo and handleRedo.
//
// In Change Layout mode all four answer from origami's own undo, which the mode keeps
// (docs/undo/MINIMUM.md item 9); otherwise from the page history.

import { createEditTimeMarkupFilter } from "./editTimeMarkup";
import { startPageHistory } from "./pageHistory";
import { attachInputTransactions } from "./pageHistoryInput";
import { attachUndoAndRedoKeys } from "./pageHistoryKeys";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
    type PageHistoryLogKind,
} from "./pageHistoryTypes";
import type { PageHistoryRecorder } from "./pageHistoryRecorder";
import {
    describeInputOpening,
    describePageHistoryDetail,
} from "./pageHistoryDetails";
import { postString } from "../../utils/bloomApi";
import { tryGetWorkspaceBundleExports } from "../js/workspaceFrames";
import { PageReadinessWaits, whenPageIsReady } from "./pageReadiness";
import {
    isInChangeLayoutMode,
    origamiCanRedo,
    origamiCanUndo,
    origamiRedo,
    origamiUndo,
} from "../js/origami";

// The history of this page, from when the page is ready until it goes.
let history: PageHistory | undefined;
// Removes the listeners that feed the history.
let detachInput: (() => void) | undefined;
// Stops following the Show Page History dialog's "Detailed logging" box.
let stopFollowingDetailedLogging: (() => void) | undefined;

/**
 * Start the page history now, and mark it ready once the page is ready (pageReadiness.ts); end it
 * when the page goes. Call once per page load, from the page frame, as soon as the page's DOM is
 * there. Until the page is ready, Bloom is still setting the page up, and a change made outside
 * any transaction is part of the page as the user found it; the user's own clicks and key presses
 * open transactions from the start, so what they do is undoable at once.
 *
 * otherWindows gives the windows other than the page frame whose input concerns the page: the
 * toolbox and the root window, with its top bar and dialogs. onStarted gets the new history at
 * once, before any input reaches it, so that components set up before the history existed can
 * listen to it.
 */
export function startPageHistoryForThisPage(
    waits: PageReadinessWaits,
    otherWindows: Window[],
    onStarted: (history: PageHistory) => void,
): void {
    const root = document.querySelector(".bloom-page");
    if (!root) throw new Error("Page history: the page has no .bloom-page.");
    // In the root window, which outlives this frame; none for a page with no workspace around it.
    const recorder = tryGetWorkspaceBundleExports()?.pageHistoryRecorder;
    recorder?.notePageLoad(root.getAttribute("id") ?? "(no id)");
    const thisHistory = startPageHistory({
        root,
        filter: createEditTimeMarkupFilter(),
        startsReady: false,
        log: (line, kind) => logLine(line, kind, recorder),
    });
    history = thisHistory;
    onStarted(thisHistory);
    if (recorder)
        stopFollowingDetailedLogging = recorder.onDetailedChange((detailed) => {
            if (thisHistory.isDisposed) return;
            thisHistory.setDetailListener(
                detailed
                    ? (detail) =>
                          describePageHistoryDetail(detail).forEach((row) =>
                              recorder.add(row.kind, row.text),
                          )
                    : undefined,
            );
        });
    const detachTransactions = attachInputTransactions(
        thisHistory,
        window,
        otherWindows,
        {
            onOpened: (event, kind) => {
                if (!recorder?.isDetailed) return;
                const row = describeInputOpening(event, kind);
                recorder.add(row.kind, row.text);
            },
        },
    );
    // After attachInputTransactions: see attachUndoAndRedoKeys.
    const detachKeys = attachUndoAndRedoKeys(window, otherWindows, {
        undo: handleUndo,
        redo: handleRedo,
        isInChangeLayoutMode,
    });
    detachInput = () => {
        detachKeys();
        detachTransactions();
    };
    window.addEventListener("pagehide", endPageHistory, { once: true });
    void whenPageIsReady(window, waits).then(() => {
        if (thisHistory.isDisposed) return;
        thisHistory.markPageReady();
        recorder?.notePageReady();
    });
}

/**
 * Send a line that the page history logged to Bloom's log and to the Show Page History dialog's
 * recorder, if there is one. A rollback goes to Bloom's log too: it takes changes off the page,
 * which a problem report needs to show.
 */
function logLine(
    line: string,
    kind: PageHistoryLogKind,
    recorder: PageHistoryRecorder | undefined,
): void {
    writeToBloomLog(line);
    recorder?.addLogLine(line, kind);
}

// The line that the page history logs when it makes a step with the general description.
const kGenericStepLogLine = `Page history: step ${kGenericUndoDescriptionL10nKey}`;
// The line that writeToBloomLog was given last.
let lastLogLine: string | undefined;

/**
 * Write a line of the page history to Bloom's log. Typing makes a step with the general
 * description at each pause, so such a step's line that comes straight after another is left out.
 */
export function writeToBloomLog(line: string): void {
    const repeated =
        line === kGenericStepLogLine && lastLogLine === kGenericStepLogLine;
    lastLogLine = line;
    if (!repeated) postString("common/logger/writeEvent", line);
}

/** Stop listening and dispose of the history, for the page unload. */
function endPageHistory(): void {
    stopFollowingDetailedLogging?.();
    stopFollowingDetailedLogging = undefined;
    detachInput?.();
    detachInput = undefined;
    history?.dispose();
    history = undefined;
}

/** Undo one step: the Undo button and Ctrl+Z. */
export function handleUndo(): void {
    if (isInChangeLayoutMode()) {
        origamiUndo();
        return;
    }
    if (history) void history.undo();
}

/**
 * Whether there is anything to undo. Cheap: C# polls it on a timer, through the workspace frame.
 * An open transaction counts: typing holds one open between key presses, and its step is made only
 * when something ends it, such as the press on the Undo button itself. Without it, the button
 * would stay disabled after the first typing on a page. A transaction that the press does not end,
 * such as a picture change waiting for the picture to load, holds the undo until it closes
 * (PageHistory.undo).
 */
export function canUndo(): boolean {
    if (isInChangeLayoutMode()) return origamiCanUndo();
    if (!history) return false;
    return (
        history.undoDescriptionL10nKey !== undefined ||
        history.hasOpenTransaction
    );
}

/** Redo one step: Ctrl+Y and Ctrl+Shift+Z. There is no Redo button. */
export function handleRedo(): void {
    if (isInChangeLayoutMode()) {
        origamiRedo();
        return;
    }
    if (history) void history.redo();
}

/** Whether there is anything to redo. */
export function canRedo(): boolean {
    if (isInChangeLayoutMode()) return origamiCanRedo();
    return history?.redoDescriptionL10nKey !== undefined;
}
