// The state of the toolbox: which tools it is offering, which of them is open, which tools
// the book has enabled, whether the toolbox is showing, and which page it is looking at.
// Which tool is *running* is not a fact of its own here: it follows from which one is open
// and which are offered (see IToolboxUiState.currentToolId).
//
// Every tool is a React component, and the toolbox UI is a React component
// (ToolboxRoot.tsx). The code that decides what goes in here — asking the server which
// tools this book has enabled, noticing that the user opened a different page or book — is
// still the non-React code in toolbox.ts. Rather than have toolbox.ts push facts into
// React, the facts live here and React subscribes: ToolboxRoot and SettingsToolControls
// read this store with React.useSyncExternalStore(), and toolbox.ts calls the mutators and
// queries below. (This is the same external-store pattern GameTool uses so that its tabs in
// the toolbox redraw; see getPanelState/subscribeToPanelState in games/GameTool.tsx.)
//
// Each tool's lifecycle then follows from this state rather than being driven by hand: the
// accordion ToolboxRoot renders for a tool runs beginRestoreSettings/showTool/newPageReady
// from an effect when it is the current tool of a showing toolbox, and detachFromPage/
// hideTool from that effect's cleanup when it stops being. See useToolLifecycle.ts.
//
// Because this module's state exists as soon as the module is loaded, there is no
// "the UI hasn't mounted yet" race to work around: toolbox.ts can offer tools and make one
// active before ToolboxRoot has mounted, and ToolboxRoot will render what it finds here.
//
// It lives in its own module (rather than in ToolboxRoot.tsx) because ToolboxRoot.tsx
// imports from toolbox.ts, so having toolbox.ts import from ToolboxRoot.tsx would create
// an import cycle.
//
// Every toolId parameter and result here is a canonical tool id, i.e. what the tool's
// ITool.id() returns, with no "Tool" suffix (e.g. "canvas", not "canvasTool"). See
// toolIds.ts for where the suffixed spellings are converted at our boundaries.
import { compareToolsByLabel, kSettingsToolId } from "./toolIds";

/**
 * The immutable snapshot React renders from. A new object is created on every change (and
 * this one is never mutated), because that is how useSyncExternalStore() tells that
 * something changed.
 */
export interface IToolboxUiState {
    // The tools the toolbox is offering, in the order it shows them:
    // alphabetical by label, with the "More..." (settings) tool last.
    readonly offeredToolIds: readonly string[];
    // The tool that is open, or undefined if none is. This is purely what the
    // UI looks like; the tool that is actually running is currentToolId.
    readonly activeToolId: string | undefined;
    // The tool that is running: the one that has been told to show itself, that is told
    // when the page changes, and whose updateMarkup() runs as the user types.
    //
    // DERIVED, not set: it is the open tool, unless the toolbox has stopped offering that
    // tool, in which case nothing is running. A tool the toolbox isn't offering has nowhere
    // to display itself, so it cannot be the running one; recording that as "no current
    // tool" rather than leaving the previous tool current is what lets returning from
    // "More..." to the same tool activate it again (BL-6720).
    //
    // Keeping this in step with activeToolId by hand is what went wrong in BL-16602, so it
    // is no longer a field anyone can set: see deriveCurrentToolId().
    //
    // Note that the tool does not stop being current when the toolbox is hidden; it just
    // stops running (see toolboxVisible).
    readonly currentToolId: string | undefined;
    // Is the toolbox sidebar showing? The current tool only runs while it is: hiding the
    // toolbox detaches and hides the tool, and showing it again restores and shows it.
    readonly toolboxVisible: boolean;
    // Bumped each time the page being edited has been replaced (or reloaded) and is ready
    // for the tools to look at. The current tool's lifecycle effect keys on this, which is
    // what gets it a fresh beginRestoreSettings/showTool/newPageReady for the new page.
    readonly pageGeneration: number;
    // The tools this book has enabled, which is what the "More..." checkboxes show.
    // Note that this is not the same as the tools being offered: tools that are always
    // enabled, and tools a page requires, are offered without being in here.
    readonly enabledToolIds: ReadonlySet<string>;
    // Has ToolboxRoot mounted, i.e. is there a toolbox on screen at all? False before it
    // mounts and after it unmounts.
    //
    // It is read by code that persists or restores a tool's settings, which must not run
    // while there is no toolbox: a tool whose panel has never been shown has no
    // user-chosen state to save, and saving then would overwrite the book's real settings
    // with defaults. The reader tools save from wherever the user changes a stage, level
    // or sort (readerToolsModel.ts), rather than from one point in their lifecycle, so
    // they ask this rather than being driven by it.
    readonly uiMounted: boolean;
}

const emptyState: IToolboxUiState = {
    offeredToolIds: [],
    activeToolId: undefined,
    currentToolId: undefined,
    toolboxVisible: false,
    pageGeneration: 0,
    enabledToolIds: new Set<string>(),
    uiMounted: false,
};

let theState: IToolboxUiState = emptyState;

const stateListeners = new Set<() => void>();

// The tool that is running, worked out from the rest of the state rather than stored. See
// IToolboxUiState.currentToolId.
//
// "Offered" is the only test needed: a tool is only ever offered after toolbox.ts has found
// it in the master list, so every offered tool is a registered one.
function deriveCurrentToolId(state: IToolboxUiState): string | undefined {
    if (
        state.activeToolId &&
        state.offeredToolIds.includes(state.activeToolId)
    ) {
        return state.activeToolId;
    }
    return undefined;
}

// Replaces the snapshot and tells the subscribers. Never mutates the old snapshot.
function updateState(changes: Partial<IToolboxUiState>): void {
    const updated = { ...theState, ...changes };
    theState = { ...updated, currentToolId: deriveCurrentToolId(updated) };
    stateListeners.forEach((listener) => listener());
}

// The order the toolbox shows its tools in: alphabetical by label, except that the
// "More..." (settings) tool always comes last.
function sortToolIdsForDisplay(toolIds: readonly string[]): string[] {
    const settingsToolIds = toolIds.filter((id) => id === kSettingsToolId);
    const otherToolIds = toolIds
        .filter((id) => id !== kSettingsToolId)
        .sort(compareToolsByLabel);
    return [...otherToolIds, ...settingsToolIds];
}

// ---------------------------------------------------------------------------
// The external store React subscribes to. These two are module functions so that their
// identity is stable: useSyncExternalStore() re-subscribes whenever they change.
// ---------------------------------------------------------------------------

export function getToolboxUiState(): IToolboxUiState {
    return theState;
}

export function subscribeToToolboxUiState(listener: () => void): () => void {
    stateListeners.add(listener);
    return () => {
        stateListeners.delete(listener);
    };
}

// ---------------------------------------------------------------------------
// Which tools the toolbox is offering
// ---------------------------------------------------------------------------

/**
 * Is the toolbox currently offering this tool? (This says nothing about whether
 * it is the active one.)
 */
export function isToolOffered(toolId: string): boolean {
    return theState.offeredToolIds.includes(toolId);
}

/**
 * The id of the first tool offered, or undefined if there are none. The "More..."
 * (settings) tool doesn't count; it is not a tool that can be current.
 */
export function getFirstOfferedToolId(): string | undefined {
    return theState.offeredToolIds.find((id) => id !== kSettingsToolId);
}

/**
 * Offers this tool. Does nothing if the toolbox is already offering it.
 */
export function offerTool(toolId: string): void {
    if (isToolOffered(toolId)) {
        return;
    }
    updateState({
        offeredToolIds: sortToolIdsForDisplay([
            ...theState.offeredToolIds,
            toolId,
        ]),
    });
}

/**
 * Stops offering this tool, if it is offered. If it was the open one, the first remaining
 * tool takes over; if none remains, no tool is open and so none is running.
 *
 * Nothing has to be told that the running tool changed: currentToolId is derived from what
 * is open and what is offered, so withdrawing a tool moves it on its own. Leaving a game
 * page withdraws the Game tool this way, and the toolbox going on believing Game was still
 * current was BL-16602.
 */
export function withdrawTool(toolId: string): void {
    const remainingToolIds = theState.offeredToolIds.filter(
        (id) => id !== toolId,
    );
    if (remainingToolIds.length === theState.offeredToolIds.length) {
        return;
    }
    if (theState.activeToolId !== toolId) {
        // We withdrew a tool the user wasn't looking at, so which tool is open doesn't
        // change -- and nor, therefore, does which one is running.
        updateState({ offeredToolIds: remainingToolIds });
        return;
    }
    // remainingToolIds[0] is undefined when nothing is left, which leaves no tool open.
    updateState({
        offeredToolIds: remainingToolIds,
        activeToolId: remainingToolIds[0],
    });
}

// ---------------------------------------------------------------------------
// Which tool is open
// ---------------------------------------------------------------------------

/**
 * Opens this tool, which also makes it the running one (see
 * IToolboxUiState.currentToolId). Every path that opens a tool comes through here, so
 * there is no way for what the user can see to drift from what is actually running; one
 * that quietly changed only what the UI shows left the two out of sync and the tool the
 * user could see was never activated (BL-16602).
 */
export function setActiveTool(toolId: string): void {
    updateState({ activeToolId: toolId });
}

// ---------------------------------------------------------------------------
// Which tool is running, whether the toolbox is showing, and which page it is on.
// Together these say whether a given tool should currently be running its lifecycle;
// see IToolboxUiState and useToolLifecycle.ts.
// ---------------------------------------------------------------------------

/** See IToolboxUiState.currentToolId. */
export function getCurrentToolId(): string | undefined {
    return theState.currentToolId;
}

/** See IToolboxUiState.toolboxVisible. */
export function isToolboxVisible(): boolean {
    return theState.toolboxVisible;
}

/**
 * Records whether the toolbox sidebar is showing. The sidebar itself is not ours (it is a
 * checkbox in the workspace frame), so toolbox.ts mirrors its state here whenever it
 * changes or is first read.
 */
export function setToolboxVisible(visible: boolean): void {
    if (theState.toolboxVisible === visible) {
        return;
    }
    updateState({ toolboxVisible: visible });
}

/** See IToolboxUiState.pageGeneration. */
export function getPageGeneration(): number {
    return theState.pageGeneration;
}

/**
 * Says that the page being edited has been replaced (or reloaded) and is now ready for the
 * tools. This is what re-runs the current tool's lifecycle for the new page, so call it
 * only once the page really is ready (toolbox.ts waits for CKEditor first).
 */
export function notePageReady(): void {
    updateState({ pageGeneration: theState.pageGeneration + 1 });
}

// ---------------------------------------------------------------------------
// Which tools the book has enabled
// ---------------------------------------------------------------------------

// Is the tool with this canonical id currently enabled?
export function isToolEnabled(toolId: string): boolean {
    return theState.enabledToolIds.has(toolId);
}

/**
 * Replaces the whole set of enabled tools, as toolbox.ts does once it has asked the server
 * which tools this book has enabled.
 */
export function setEnabledTools(toolIds: Iterable<string>): void {
    updateState({ enabledToolIds: new Set(toolIds) });
}

// Records that the user has turned this tool on or off in the "More..." tool.
export function setToolEnabled(toolId: string, enabled: boolean): void {
    const enabledToolIds = new Set(theState.enabledToolIds);
    if (enabled) {
        enabledToolIds.add(toolId);
    } else {
        enabledToolIds.delete(toolId);
    }
    updateState({ enabledToolIds });
}

// ---------------------------------------------------------------------------
// Whether the toolbox UI exists
// ---------------------------------------------------------------------------

export function isToolboxUiMounted(): boolean {
    return theState.uiMounted;
}

// Called by ToolboxRoot as it mounts and unmounts.
export function setToolboxUiMounted(mounted: boolean): void {
    updateState({ uiMounted: mounted });
}

// ---------------------------------------------------------------------------

/**
 * Test-only: puts the store back the way it was at module load. Unit tests share one
 * instance of this module across the tests in a file, so a test that wants to start from
 * an empty toolbox must say so.
 */
export function resetToolboxUiStateForTests(): void {
    theState = emptyState;
    stateListeners.clear();
}
