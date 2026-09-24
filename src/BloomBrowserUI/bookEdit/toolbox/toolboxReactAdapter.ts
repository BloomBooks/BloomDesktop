// The root of the toolbox is a React component (ToolboxRoot.tsx), but a good deal of the
// toolbox is still the legacy, non-React code in toolbox.ts. This module is the single
// narrow channel between the two: ToolboxRoot registers an implementation of
// IToolboxReactAdapter when it mounts, and the legacy code uses it to make a tool active
// and to be notified when the user makes a different tool active.
//
// It lives in its own module (rather than being exported from ToolboxRoot.tsx) because
// ToolboxRoot.tsx imports from toolbox.ts, so having toolbox.ts import from ToolboxRoot.tsx
// would create an import cycle.
//
// When all the tools are React components, each one will belong to its own accordion
// section and manage its own state and lifecycle, and this module can go away.
export interface IToolboxReactAdapter {
    // Makes the tool with this id (with or without the "Tool" suffix) the active,
    // expanded section of the React accordion.
    setActiveToolByToolId(toolId: string): void;
    // Registers a callback to be told whenever the active tool changes, including
    // as a result of setActiveToolByToolId().
    onActiveToolChanged(callback: (toolId: string) => void): void;
}

let theOneToolboxReactAdapter: IToolboxReactAdapter | undefined;

/**
 * Called by ToolboxRoot once it has mounted (and again whenever the state it closes
 * over changes), making the adapter available to the legacy toolbox code.
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
