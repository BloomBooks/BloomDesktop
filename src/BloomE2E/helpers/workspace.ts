// Drive and read Bloom's top-bar workspace tabs.
//
// The WinForms shell, not the React top bar, owns which tab is active. So a test clicks the real
// button (switchTab) but asks Bloom's own workspace/tabs API what happened (getTabs,
// waitForActiveTab) instead of inferring it from the DOM. That is the assertion half of the
// UI-vs-API policy in README.md, and it is also the only way to know that the WinForms side has
// finished the switch rather than merely started it.

import { expect, type Page } from "@playwright/test";
import { apiGetJson } from "./api";

/** The three workspace tabs, named as Bloom's API names them. */
export type WorkspaceTabId = "collection" | "edit" | "publish";

/** A tab's state, as workspace/tabs reports it. */
export type WorkspaceTabState = "active" | "enabled" | "disabled" | "hidden";

/** What GET workspace/tabs replies with. */
export interface IWorkspaceTabs {
    tabStates: Record<WorkspaceTabId, WorkspaceTabState>;
    /** True while something modal has locked navigation (an upload, an Edit-tab dialog). */
    navigationLocked: boolean;
}

// The test id on each tab in the top bar, set in react_components/TopBar/TopBar.tsx. The tab ids
// here are Bloom's own API names, and the test ids are built from them, so this needs no map.
function tabTestId(tab: WorkspaceTabId): string {
    return `workspace-tab-${tab}`;
}

/** Ask Bloom which workspace tab is active and what state the others are in. */
export async function getTabs(page: Page): Promise<IWorkspaceTabs> {
    return apiGetJson<IWorkspaceTabs>(page, "workspace/tabs");
}

/**
 * Wait until `tab` is the active workspace tab. Use it after switchTab, and after anything else
 * that changes tabs, because the WinForms side switches asynchronously.
 */
export async function waitForActiveTab(
    page: Page,
    tab: WorkspaceTabId,
    timeoutMs = 30000,
): Promise<void> {
    await expect
        .poll(async () => (await getTabs(page)).tabStates[tab], {
            timeout: timeoutMs,
            message: `Bloom's workspace tab '${tab}' never became active.`,
        })
        .toBe("active");
}

/**
 * Switch to a workspace tab by clicking its real top-bar tab, then wait for Bloom to report it
 * active. This is the user's own path; nothing here posts workspace/selectTab.
 *
 * Bloom hides the Edit and Publish tabs entirely until a book is selected, so a test that wants
 * either of them must select a book first (see helpers/collection.ts).
 *
 * The tab is found by its test id, not by its label, so this works in any UI language.
 */
export async function switchTab(
    page: Page,
    tab: WorkspaceTabId,
    timeoutMs = 30000,
): Promise<void> {
    const target = page.getByTestId(tabTestId(tab));
    await target.waitFor({ state: "visible", timeout: timeoutMs });
    await target.click();
    await waitForActiveTab(page, tab, timeoutMs);
}
