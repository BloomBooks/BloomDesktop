// Every tool is a React component, and the toolbox UI is a React component
// (ToolboxRoot.tsx), but the code that orchestrates the toolbox — asking the server which
// tools this book has enabled, and running each tool's lifecycle as pages, books and tools
// change — is still the non-React code in toolbox.ts. This module is the single narrow
// channel between the two: ToolboxRoot registers an implementation of IToolboxReactAdapter
// when it mounts, and toolbox.ts uses it to say which tools the toolbox is offering, to
// make one of them active, and to be notified when the user makes a different tool active.
//
// It lives in its own module (rather than being exported from ToolboxRoot.tsx) because
// ToolboxRoot.tsx imports from toolbox.ts, so having toolbox.ts import from ToolboxRoot.tsx
// would create an import cycle.
//
// Every toolId parameter and result here is a canonical tool id, i.e. what the tool's
// ITool.id() returns, with no "Tool" suffix (e.g. "canvas", not "canvasTool"). See
// toolIds.ts for where the suffixed spellings are converted at our boundaries.
export interface IToolboxReactAdapter {
    // Makes the tool with this id the active, expanded section of the React accordion.
    setActiveToolByToolId(toolId: string): void;
    // Registers a callback to be told whenever the active tool changes, including
    // as a result of setActiveToolByToolId().
    onActiveToolChanged(callback: (toolId: string) => void): void;
    // Adds a section for this tool, building its body from the tool's makeRootElement().
    // Does nothing if the toolbox is already offering the tool.
    addTool(toolId: string): void;
    // Removes this tool's section, if it has one. If it was the active section, the first
    // remaining tool becomes active.
    removeTool(toolId: string): void;
    // Is the toolbox currently offering a section for this tool?
    hasTool(toolId: string): boolean;
    // The id of the first tool section, or undefined if there are no tool sections.
    // The "More..." (settings) section doesn't count; it is not a tool that can be current.
    getFirstToolId(): string | undefined;
}

let theOneToolboxReactAdapter: IToolboxReactAdapter | undefined;

// Actions given to whenToolboxReactAdapterReady() before ToolboxRoot had mounted. Each is
// run (and forgotten) as soon as it has.
const actionsWaitingForAdapter: ((adapter: IToolboxReactAdapter) => void)[] =
    [];

/**
 * Called by ToolboxRoot once it has mounted, making the adapter available to the
 * orchestration code in toolbox.ts.
 */
export function setToolboxReactAdapter(adapter: IToolboxReactAdapter): void {
    theOneToolboxReactAdapter = adapter;
    actionsWaitingForAdapter.splice(0).forEach((action) => action(adapter));
}

/**
 * Runs the action as soon as ToolboxRoot has published its adapter (immediately, if it
 * already has). Startup renders ToolboxRoot before initializing the rest of the toolbox,
 * but React mounts asynchronously, so code that must not silently do nothing (in
 * particular, populating the toolbox with the book's tools) waits here rather than
 * assuming the adapter already exists.
 */
export function whenToolboxReactAdapterReady(
    action: (adapter: IToolboxReactAdapter) => void,
): void {
    if (theOneToolboxReactAdapter) {
        action(theOneToolboxReactAdapter);
        return;
    }
    actionsWaitingForAdapter.push(action);
}

/**
 * The adapter published by ToolboxRoot. Returns undefined only until ToolboxRoot has
 * mounted; in practice that is before anything asks for it, since toolboxBootstrap
 * renders ToolboxRoot before initializing toolbox.ts, and toolbox.ts only asks for the
 * adapter in response to a user action or an API response. Callers must still allow for
 * undefined, but need not do anything useful in that case.
 */
export function getToolboxReactAdapter(): IToolboxReactAdapter | undefined {
    return theOneToolboxReactAdapter;
}

/**
 * Has the toolbox UI been created? Code that persists or restores toolbox state can use
 * this to tell "we are running in the real toolbox" from "we are running in a unit test
 * (or too early in startup) where there is no toolbox UI and nothing should be saved".
 */
export function isToolboxUiReady(): boolean {
    return !!theOneToolboxReactAdapter;
}
