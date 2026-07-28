// The state of the toolbox UI: which tools it is offering, which of them is expanded,
// which tools the book has enabled, and whether the UI exists yet.
//
// Every tool is a React component, and the toolbox UI is a React component
// (ToolboxRoot.tsx), but the code that orchestrates the toolbox — asking the server which
// tools this book has enabled, and running each tool's lifecycle as pages, books and tools
// change — is still the non-React code in toolbox.ts. Rather than have toolbox.ts push
// facts into React, the facts live here and React subscribes: ToolboxRoot and
// SettingsToolControls read this store with React.useSyncExternalStore(), and toolbox.ts
// calls the mutators and queries below. (This is the same external-store pattern the Game
// tool uses for its panel; see getPanelState/subscribeToPanelState in games/GameTool.tsx.)
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
    // The tools the toolbox is offering a section for, in the order it shows them:
    // alphabetical by label, with the "More..." (settings) section last.
    readonly offeredToolIds: readonly string[];
    // The tool whose section is expanded, or undefined if none is.
    readonly activeToolId: string | undefined;
    // The tools this book has enabled, which is what the "More..." checkboxes show.
    // Note that this is not the same as the tools being offered: tools that are always
    // enabled, and tools a page requires, get a section without being in here.
    readonly enabledToolIds: ReadonlySet<string>;
    // Has ToolboxRoot mounted? Code that persists or restores toolbox state uses this to
    // tell "we are running in the real toolbox" from "we are running in a unit test (or
    // too early in startup) where there is no toolbox UI and nothing should be saved".
    readonly uiMounted: boolean;
}

const emptyState: IToolboxUiState = {
    offeredToolIds: [],
    activeToolId: undefined,
    enabledToolIds: new Set<string>(),
    uiMounted: false,
};

let theState: IToolboxUiState = emptyState;

const stateListeners = new Set<() => void>();

// Told whenever a tool becomes the active one. toolbox.ts subscribes here to drive each
// tool's showTool()/hideTool() lifecycle; see the comment on setActiveTool().
const activeToolListeners = new Set<(toolId: string) => void>();

// Replaces the snapshot and tells the subscribers. Never mutates the old snapshot.
function updateState(changes: Partial<IToolboxUiState>): void {
    theState = { ...theState, ...changes };
    stateListeners.forEach((listener) => listener());
}

// The order the toolbox shows its sections in: alphabetical by label, except that the
// "More..." (settings) section always comes last.
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
 * Is the toolbox currently offering this tool a section? (This says nothing about whether
 * it is the active one.)
 */
export function isToolOffered(toolId: string): boolean {
    return theState.offeredToolIds.includes(toolId);
}

/**
 * The id of the first tool section, or undefined if there are none. The "More..."
 * (settings) section doesn't count; it is not a tool that can be current.
 */
export function getFirstOfferedToolId(): string | undefined {
    return theState.offeredToolIds.find((id) => id !== kSettingsToolId);
}

/**
 * Offers a section for this tool. Does nothing if the toolbox is already offering it.
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
 * Stops offering this tool's section, if it has one. If it was the active section, the
 * first remaining section becomes active (and that is reported to the active-tool
 * listeners, i.e. to toolbox.ts).
 */
export function withdrawTool(toolId: string): void {
    const remainingToolIds = theState.offeredToolIds.filter(
        (id) => id !== toolId,
    );
    if (remainingToolIds.length === theState.offeredToolIds.length) {
        return;
    }
    if (theState.activeToolId !== toolId) {
        // We withdrew a tool the user wasn't looking at, so which section is open
        // doesn't change.
        updateState({ offeredToolIds: remainingToolIds });
        return;
    }
    const replacementToolId = remainingToolIds[0];
    if (!replacementToolId) {
        // Nothing left to open. Don't notify toolbox.ts: it has no way to represent
        // "no current tool", and expanding a section later will tell it then.
        updateState({
            offeredToolIds: remainingToolIds,
            activeToolId: undefined,
        });
        return;
    }
    // Notify, so toolbox.ts hears about the replacement. Leaving a game page withdraws
    // the Game tool this way, and when this didn't notify, toolbox.ts went on believing
    // Game was current and never called showTool() on the tool that replaced it, which
    // killed Talking Book's highlighting and audio (BL-16602).
    updateState({
        offeredToolIds: remainingToolIds,
        activeToolId: replacementToolId,
    });
    notifyActiveToolListeners(replacementToolId);
}

// ---------------------------------------------------------------------------
// Which tool is active (expanded)
// ---------------------------------------------------------------------------

/**
 * Runs the callback whenever a tool becomes the active one. Returns a function that
 * unsubscribes.
 */
export function subscribeToActiveToolChanges(
    callback: (toolId: string) => void,
): () => void {
    activeToolListeners.add(callback);
    return () => {
        activeToolListeners.delete(callback);
    };
}

function notifyActiveToolListeners(toolId: string): void {
    activeToolListeners.forEach((listener) => listener(toolId));
}

/**
 * Expands this tool's section and tells the active-tool listeners about it. toolbox.ts
 * keeps its own idea of which tool is current and drives each tool's showTool()/hideTool()
 * from it, so every path that makes a real tool the active one has to come through here;
 * one that quietly changed only what the UI shows left the two out of sync and the tool
 * the user could see was never activated (BL-16602).
 */
export function setActiveTool(toolId: string): void {
    updateState({ activeToolId: toolId });
    notifyActiveToolListeners(toolId);
}

/**
 * Collapses whatever section is open, leaving none active. Deliberately does not notify
 * the active-tool listeners: toolbox.ts has no way to represent "no current tool", and
 * expanding a section later will tell it then.
 */
export function clearActiveTool(): void {
    updateState({ activeToolId: undefined });
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

// Records that the user has turned this tool on or off in the "More..." section.
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
    activeToolListeners.clear();
}
