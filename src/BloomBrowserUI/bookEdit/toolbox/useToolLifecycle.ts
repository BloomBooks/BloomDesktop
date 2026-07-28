// Runs one tool's lifecycle as a consequence of rendering, instead of having toolbox.ts
// call the tool's lifecycle methods by hand.
//
// ToolboxRoot renders a section for every tool the toolbox is offering, and each of those
// sections calls this hook. A tool runs when it is the current tool of a showing toolbox
// (see IToolboxUiState.currentToolId / toolboxVisible), so it activates because it *is* the
// current tool rather than because some other code remembered to activate it. That
// remembering is exactly what got out of step in BL-16602.
//
// Note that being the current tool is not the same as being mounted: MUI keeps a collapsed
// accordion section's children mounted, and ToolboxRoot relies on that so a tool keeps its
// state while another one is open. So the sections of all the offered tools call this hook
// all the time; only one of them is running.
//
// What is deliberately NOT here:
//  - updateMarkup()/updateMarkupAsync(): called synchronously by the markup engine on
//    keystrokes, with ordering and performance constraints of their own. See
//    pageEditingMarkup.ts.
//  - configureElements(): the toolbox calls it on every registered tool when a page is set
//    up, open or not, so it is not a function of which tool is running.
//  - imageUpdated(): raised by the page frame against whichever tool is current.
//  - the detachFromPage() that has to happen just *before* the page is replaced (see
//    removeToolboxMarkup in toolbox.ts). React only finds out that the page changed
//    afterwards, by which time the DOM to detach from is gone, so that one cannot be a
//    render-driven cleanup and remains an explicit call.
import * as React from "react";
import { postString } from "../../utils/bloomApi";
import {
    getSavedToolboxSettings,
    ITool,
    runTasksForClosingTool,
} from "./toolbox";

// How long after telling a tool the page is ready we tell it again.
// The toolbox has always done this to get around a race we never tracked down, in which
// some tools' first look at a new page came too early. Tools are expected to tolerate it
// and not repeat work they have already done (see ITool.newPageReady).
const kRepeatNewPageReadyDelayMs = 600;

/**
 * Runs `tool`'s lifecycle while it is the running tool.
 *
 * @param tool the tool this section belongs to.
 * @param isRunning is this the current tool of a showing toolbox? While it is,
 *   the tool is restored and shown; when it stops being, the tool is detached and hidden.
 * @param pageGeneration IToolboxUiState.pageGeneration. Changing it re-runs the sequence
 *   for the new page, which is what tells the tool about it.
 */
export function useToolLifecycle(
    tool: ITool,
    isRunning: boolean,
    pageGeneration: number,
): void {
    // Restoring, showing and page-ready. This effect is declared FIRST so that when the
    // tool stops running, its cleanup (which stops the sequence below) runs before the
    // detach-and-hide cleanup of the effect after it: React runs a fiber's effect cleanups
    // in the order the effects were declared.
    //
    // The whole sequence is one async function because its steps are ordered by their
    // promises: a tool is not ready to be shown until beginRestoreSettings() resolves, and
    // newPageReady() must not run until showTool() has (talkingBookTool's is async, and
    // BL-10471 was a flash caused by not waiting).
    //
    // pageGeneration is a dependency because a new page needs all three again, not just
    // newPageReady(): some tools do their page-dependent setup in showTool(), and the tool's
    // saved state has to be re-read in case the page belongs to a different book. That is
    // what the toolbox has always done when a page was replaced.
    React.useEffect(() => {
        if (!isRunning) {
            return undefined;
        }
        // Set by the cleanup. Everything after an await checks it, so that a tool that is
        // no longer running (or is now looking at a different page) doesn't go on being set
        // up over the top of whatever replaced it.
        let stopped = false;
        const runActivationSequence = async () => {
            // Always re-restore settings, even if this tool was current in the previous
            // book, so that its state tracks the book we are now in.
            await tool.beginRestoreSettings(getSavedToolboxSettings());
            if (stopped) return;
            await tool.showTool();
            if (stopped) return;
            await tool.newPageReady();
            if (stopped) return;
            window.setTimeout(() => {
                if (stopped) return;
                Promise.resolve(tool.newPageReady());
            }, kRepeatNewPageReadyDelayMs);
        };
        runActivationSequence();
        return () => {
            stopped = true;
        };
    }, [tool, isRunning, pageGeneration]);

    // Being the running tool. There is nothing to do on the way in that the effect above
    // doesn't do; this effect exists for its cleanup, which is what happens when the tool
    // stops running: switching to another tool, hiding the toolbox, or the toolbox no
    // longer offering this tool at all (leaving a game page does that to the Game tool).
    //
    // Deliberately NOT keyed on pageGeneration: a new page detaches and re-attaches the
    // tool (see removeToolboxMarkup), but it does not hide it.
    //
    // React runs every cleanup in a commit before any effect in that commit — including
    // the cleanups of components it is removing — so the tool being switched away from is
    // always detached and hidden before the incoming one starts restoring and showing.
    React.useEffect(() => {
        if (!isRunning) {
            return undefined;
        }
        // Logged from here rather than from the sequence above so that a user paging
        // through a book doesn't fill the log with one of these per page. (It therefore
        // lands as the tool starts being shown rather than once showTool() has finished.)
        postString("logger/writeEvent", `Toolbox activated: ${tool.id()}`);
        return () => {
            // Popups and dialogs that registered to be closed when the tool goes away.
            runTasksForClosingTool();
            tool.detachFromPage();
            tool.hideTool();
        };
    }, [tool, isRunning]);
}
