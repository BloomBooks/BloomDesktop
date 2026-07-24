// The root of the toolbox is a React component (ToolboxRoot.tsx), but a good deal of the
// toolbox is still the legacy, non-React code in toolbox.ts. This module is the single
// narrow channel between the two: ToolboxRoot registers an implementation of
// IToolboxReactAdapter when it mounts, and the legacy code uses it to say which tools the
// toolbox is offering, to make one of them active, and to be notified when the user makes
// a different tool active.
//
// It lives in its own module (rather than being exported from ToolboxRoot.tsx) because
// ToolboxRoot.tsx imports from toolbox.ts, so having toolbox.ts import from ToolboxRoot.tsx
// would create an import cycle.
//
// When all the tools are React components, each one will belong to its own accordion
// section and manage its own state and lifecycle, and this module can go away.
//
// Every toolId parameter and result here may be spelled with or without the historical
// "Tool" suffix ("canvas" and "canvasTool" mean the same tool); the implementation
// normalizes them.
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
    // The id (with the "Tool" suffix) of the first tool section, or undefined if there
    // are no tool sections. The "More..." (settings) section doesn't count; it is not a
    // tool that can be current.
    getFirstToolId(): string | undefined;
}

let theOneToolboxReactAdapter: IToolboxReactAdapter | undefined;

/**
 * Called by ToolboxRoot once it has mounted, making the adapter available to the legacy
 * toolbox code.
 */
export function setToolboxReactAdapter(adapter: IToolboxReactAdapter): void {
    theOneToolboxReactAdapter = adapter;
}

/**
 * The adapter published by ToolboxRoot. Returns undefined only until ToolboxRoot has
 * mounted; in practice that is before anything asks for it, since toolboxBootstrap
 * renders ToolboxRoot before initializing the legacy toolbox, and the legacy code only
 * asks for the adapter in response to a user action or an API response. Callers must
 * still allow for undefined, but need not do anything useful in that case.
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
