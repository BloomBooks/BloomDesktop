// The state of the toolbox: which tools it is offering, which of them is expanded, which tools
// the book has enabled, whether the toolbox is showing, and which page it is looking at.
// Which tool is *running* is not a fact of its own here: it follows from which one is
// expanded and whether the toolbox is showing.
//
// Every tool is a React component, and the toolbox UI is a React component
// (ToolboxRoot.tsx). The code that decides what goes in here -- asking the server which
// tools this book has enabled, noticing that the user opened a different page or book -- is
// still the non-React code in toolbox.ts. Rather than have toolbox.ts push facts into
// React, the facts live here and React subscribes: ToolboxRoot and SettingsToolControls
// read this store with React.useSyncExternalStore(), and toolbox.ts calls the mutators and
// queries below. (This is the same external-store pattern GameTool uses so that its tabs in
// the toolbox redraw; see getPanelState/subscribeToPanelState in games/GameTool.tsx.)
//
// Each tool's lifecycle then follows from this state rather than being driven by hand: the
// accordion ToolboxRoot renders for a tool calls beginRestoreSettings/showTool/newPageReady
// from an effect while that tool is running -- expanded, with the toolbox showing -- and
// detachFromPage/hideTool from that effect's cleanup when it stops being. See useToolLifecycle.ts.
//
// Because this module's state exists as soon as the module is loaded, there is no
// "the UI hasn't mounted yet" race to work around: toolbox.ts can offer tools and expand
// one before ToolboxRoot has mounted, and ToolboxRoot will render what it finds here.
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
 * Four words describe a tool's relationship to the toolbox, and they mean different things.
 * Defined here once; everything else in the toolbox uses them this way.
 *
 * - ENABLED: the book has this tool turned on, which is what the "More..." checkboxes set and
 *   what the book remembers. This is the only one of the four the user sets directly. It is
 *   NOT the same as offered: a tool can be enabled without being offered (we defer offering
 *   it briefly so the checkbox does not appear to un-tick itself), and offered without being
 *   enabled (Talking Book always is, and a page can require a tool the book never enabled).
 * - OFFERED: the toolbox is showing this tool in its list, because the book has enabled it or
 *   because the page being edited requires it. What is offered changes as you work: the Game
 *   tool is offered on arriving at a game page and withdrawn on leaving it.
 * - EXPANDED: the one accordion section that is expanded, and so the tool the user has chosen to
 *   work with. It stays expanded while the toolbox itself is hidden: the section has not
 *   collapsed, the whole toolbox has slid off screen, so nobody can see it. That gap between
 *   "chosen" and "on screen" is what RUNNING is for.
 *
 *   Always a tool the toolbox is offering -- setExpandedTool() refuses any other, and
 *   withdrawing the expanded tool moves this on -- so there is no second, re-checked notion of
 *   a "current" tool.
 *
 *   We say expanded rather than open because "open" is already taken, by the toolbox itself:
 *   a book remembers whether its toolbox was open (meta.json's toolboxIsOpen), so "the open
 *   tool of an open toolbox" would be two different states wearing one word. Expanded is also
 *   what the accordion answers to -- aria-expanded in the DOM, Mui-expanded in the CSS, MUI's
 *   own `expanded` prop. (meta.json calls the remembered tool "current", which is a third
 *   word again; toolIds.ts converts it at the boundary.)
 * - RUNNING: the expanded tool, while the toolbox is showing -- getRunningToolIdOf() below.
 *   Only a running tool has been told to show itself, hears about page changes, and marks up
 *   the page as you type. Hiding the toolbox stops a tool running without collapsing it.
 *   useToolLifecycle.ts is what starts and stops it.
 *
 * Separately, toolbox.ts tracks the tool a *loading page* requires, which outranks the
 * expanded one until the toolbox moves on; getEffectiveToolId() is that question, and it is
 * none of the four above.
 */

/**
 * The immutable snapshot React renders from. A new object is created on every change (and
 * this one is never mutated), because that is how useSyncExternalStore() tells that
 * something changed.
 */
export interface IToolboxUiState {
    // The tools the toolbox is offering, in the order it shows them:
    // alphabetical by label, with the "More..." (settings) tool last.
    readonly offeredToolIds: readonly string[];
    // EXPANDED: the one accordion section that is expanded, or undefined if none is. Always a
    // tool the toolbox is offering; see the definitions above.
    readonly expandedToolId: string | undefined;
    // Is the toolbox sidebar showing? The expanded tool only runs while it is: hiding the
    // toolbox detaches and hides the tool, and showing it again restores and shows it.
    readonly toolboxVisible: boolean;
    // Bumped each time the page being edited has been replaced (or reloaded) and is ready
    // for the tools to look at. The running tool's lifecycle effect keys on this, which is
    // what gets it a fresh beginRestoreSettings/showTool/newPageReady for the new page.
    readonly pageGeneration: number;
    // The tools this book has enabled, which is what the "More..." checkboxes show.
    // Note that this is not the same as the tools being offered: tools that are always
    // enabled, and tools a page requires, are offered without being in here.
    readonly enabledToolIds: ReadonlySet<string>;
}

const emptyState: IToolboxUiState = {
    offeredToolIds: [],
    expandedToolId: undefined,
    toolboxVisible: false,
    pageGeneration: 0,
    enabledToolIds: new Set<string>(),
};

let theState: IToolboxUiState = emptyState;

const stateListeners = new Set<() => void>();

// Replaces the snapshot and tells the subscribers. Never mutates the old snapshot.
function updateState(changes: Partial<IToolboxUiState>): void {
    theState = { ...theState, ...changes };
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
 * Is the toolbox offering this tool -- that is, is it in the list the user can see? A tool is
 * offered because the book has turned it on, or because the page being edited requires it.
 * That second reason is why this genuinely changes from moment to moment: arriving at a game
 * page offers the Game tool, and leaving withdraws it again.
 *
 * This is not the question "does this Bloom implement that tool?", which is the master tool
 * list. It says nothing about whether the tool is expanded or running.
 */
export function isToolOffered(toolId: string): boolean {
    return theState.offeredToolIds.includes(toolId);
}

/**
 * The id of the first tool offered, or undefined if there are none. The "More..." (settings)
 * tool doesn't count: it is never chosen as the *default* tool. Opening it by hand does make
 * it the expanded one, and that gets persisted (meta.json calls it "current"), as it always has.
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
 * Stops offering this tool, if it is offered. If it was the expanded one, the first remaining
 * tool takes over; if none remains, nothing is expanded and so nothing is running.
 *
 * Nothing has to be told that the expanded tool changed: which tool runs is worked out from
 * what is expanded and whether the toolbox is showing, so withdrawing a tool moves it on its
 * own. Leaving a game page withdraws the Game tool this way, and the toolbox going on
 * believing Game was still expanded was BL-16602.
 */
export function withdrawTool(toolId: string): void {
    const remainingToolIds = theState.offeredToolIds.filter(
        (id) => id !== toolId,
    );
    if (remainingToolIds.length === theState.offeredToolIds.length) {
        return;
    }
    if (theState.expandedToolId !== toolId) {
        // We withdrew a tool the user wasn't looking at, so which tool is expanded doesn't
        // change -- and nor, therefore, does which one is running.
        updateState({ offeredToolIds: remainingToolIds });
        return;
    }
    // Fall back to whatever is now first. remainingToolIds[0] would be undefined if nothing
    // were left, which in practice cannot happen: Talking Book and "More..." are always
    // offered. Nothing expanded is the sensible answer in that case anyway.
    //
    // Offered and expanded change together here, in one snapshot, so that no subscriber ever
    // sees the moment in between -- which is why this does not call setExpandedTool().
    updateState({
        offeredToolIds: remainingToolIds,
        expandedToolId: remainingToolIds[0],
    });
}

// ---------------------------------------------------------------------------
// Which tool is expanded
// ---------------------------------------------------------------------------

/**
 * Expands this tool, which is how a tool is chosen. This is the only way in: `expandedToolId`
 * is private to this module, and the only other place that moves it is withdrawTool, which has
 * to change it in the same snapshot as the offered list.
 *
 * A tool the toolbox is not offering cannot be expanded, and asking for one is refused rather
 * than obeyed. It has no accordion section at all, so obeying would put the store in a
 * state the screen cannot show -- which is exactly the drift that left the tool the user
 * could see un-activated in BL-16602. Deciding what to expand *instead* is policy and stays
 * with the caller (see expandToolOrFirstOffered in toolbox.ts, which falls back to the first
 * offered tool). We log rather than throw: this is a programming error on a UI path, and taking the
 * Edit tab down over it would be worse than ignoring the request.
 */
export function setExpandedTool(toolId: string): void {
    if (!isToolOffered(toolId)) {
        console.error(
            `setExpandedTool("${toolId}") ignored: the toolbox is not offering that tool. ` +
                `It is offering: ${theState.offeredToolIds.join(", ") || "(nothing)"}. ` +
                `Offer a tool before expanding it.`,
        );
        return;
    }
    updateState({ expandedToolId: toolId });
}

// ---------------------------------------------------------------------------
// Which tool is running, whether the toolbox is showing, and which page it is on.
// Together these say whether a given tool should currently be running its lifecycle;
// see IToolboxUiState and useToolLifecycle.ts.
// ---------------------------------------------------------------------------

/** See IToolboxUiState.expandedToolId. */
export function getExpandedToolId(): string | undefined {
    return theState.expandedToolId;
}

/**
 * The RUNNING tool of a snapshot: the expanded one, while the toolbox is showing. A tool only
 * runs while this names it -- it is told to show itself, hears about page changes, and marks
 * up the page as the user types. React renders from a snapshot, so it asks this of the one it
 * holds; code outside React asks ToolBox.getRunningTool() instead, which reads the same fact
 * from the checkbox the store mirrors.
 */
export function getRunningToolIdOf(state: IToolboxUiState): string | undefined {
    return state.toolboxVisible ? state.expandedToolId : undefined;
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

/**
 * Says that the page being edited has been replaced (or reloaded) and is now ready for the
 * tools. This is what re-runs the running tool's lifecycle for the new page, so call it
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

// Has ToolboxRoot mounted, i.e. is there a toolbox on screen at all? False before it
// mounts and after it unmounts.
//
// It is read by code that persists or restores a tool's settings, which must not run
// while there is no toolbox: a tool whose panel has never been shown has no
// user-chosen state to save, and saving then would overwrite the book's real settings
// with defaults. The reader tools save from wherever the user changes a stage, level
// or sort (readerToolsModel.ts), rather than from one point in their lifecycle, so
// they ask this rather than being driven by it.
//
// It is deliberately not part of the snapshot: nothing renders from it, so putting it
// there would notify every subscriber -- re-rendering the toolbox -- to record a fact
// the toolbox does not display.
let uiMounted = false;

export function isToolboxUiMounted(): boolean {
    return uiMounted;
}

// Called by ToolboxRoot as it mounts and unmounts.
export function setToolboxUiMounted(mounted: boolean): void {
    uiMounted = mounted;
}

// ---------------------------------------------------------------------------

/**
 * Test-only: puts the store back the way it was at module load. Unit tests share one
 * instance of this module across the tests in a file, so a test that wants to start from
 * an empty toolbox must say so.
 */
export function resetToolboxUiStateForTests(): void {
    theState = emptyState;
    uiMounted = false;
    stateListeners.clear();
}
