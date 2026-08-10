import { useWatchApiObject } from "../../utils/bloomApi";

// The workspace-navigation state shared between TopBar and other screens.
// This module deliberately has no side effects, so screens that only need the
// hook (e.g. PublishTabPane, CollectionsTabBookPane) can import it without
// pulling in TopBar.tsx's module-scope WireUpForWinforms registration.

export type WorkspaceTabId = "collection" | "edit" | "publish";

export type WorkspaceTabState = "active" | "enabled" | "disabled" | "hidden";

export type TabStates = Record<WorkspaceTabId, WorkspaceTabState>;

// What C# (WorkspaceView.GetTabInfo) tells us about workspace navigation.
export interface IWorkspaceTabInfo {
    tabStates: TabStates;
    // True while some operation has made itself modal by locking navigation: a BloomLibrary
    // upload, a Reading App Builder action, or an Edit-tab modal dialog. Screens with their own
    // navigation (notably the Publish tab's switcher between publish tools) use this to lock in
    // step with the main tabs.
    navigationLocked: boolean;
}

const kWorkspaceTabIds: WorkspaceTabId[] = ["collection", "edit", "publish"];

export function getActiveWorkspaceTab(tabStates: TabStates): WorkspaceTabId {
    return (
        kWorkspaceTabIds.find((id) => tabStates[id] === "active") ??
        "collection"
    );
}

export const defaultWorkspaceTabState: IWorkspaceTabInfo = {
    tabStates: {
        collection: "active",
        edit: "hidden",
        publish: "hidden",
    },
    navigationLocked: false,
};

// Subscribes to what C# says about workspace navigation, kept in one place because several
// screens in different browser controls need the same answer.
export function useWorkspaceTabInfo(): IWorkspaceTabInfo {
    return useWatchApiObject<IWorkspaceTabInfo>(
        "workspace/tabs",
        defaultWorkspaceTabState,
        "workspace",
        "tabs",
    );
}
