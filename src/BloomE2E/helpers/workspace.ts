// Drive and read Bloom's top bar: the workspace tabs, and the zoom control at its right end.
//
// The WinForms shell, not the React top bar, owns which tab is active. So a test clicks the real
// button (switchTab) but asks Bloom's own workspace/tabs API what happened (getTabs,
// waitForActiveTab) instead of inferring it from the DOM. That is the assertion half of the
// UI-vs-API policy in README.md, and it is also the only way to know that the WinForms side has
// finished the switch rather than merely started it.

import { expect, type Page } from "@playwright/test";
import { apiGetJson, apiPost } from "./api";
import { editablePageFrame } from "./bookMaking";

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

// The visible label on each tab in the top bar. These differ from the API's tab ids, which is why
// a test names the id and this map does the translating. The labels are localized, so a run in
// another UI language would need this to come from the l10n data instead.
const TAB_LABEL: Record<WorkspaceTabId, string> = {
    collection: "Collections",
    edit: "Edit",
    publish: "Publish",
};

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
 */
export async function switchTab(
    page: Page,
    tab: WorkspaceTabId,
    timeoutMs = 30000,
): Promise<void> {
    const target = page.getByRole("tab", { name: TAB_LABEL[tab] });
    await target.waitFor({ state: "visible", timeout: timeoutMs });
    await target.click();
    await waitForActiveTab(page, tab, timeoutMs);
}

/** What GET workspace/topRight/zoom replies with: the Edit tab's zoom, as a percentage. */
export interface IZoomInfo {
    zoom: number;
    minZoom: number;
    maxZoom: number;
    /** False outside the Edit tab, where the top bar hides the zoom control. */
    zoomEnabled: boolean;
}

/** Ask Bloom what the Edit tab's zoom is, and the range it allows. */
export async function getZoom(page: Page): Promise<IZoomInfo> {
    return apiGetJson<IZoomInfo>(page, "workspace/topRight/zoom");
}

/**
 * Set the Edit tab's zoom to `percent`, by the same route as the top bar's + and − buttons post.
 * This is the SETUP route for a test that needs the page drawn large or small; a test whose subject
 * is the zoom control clicks the buttons instead.
 *
 * Returns once Bloom reports the new zoom and the page being edited is drawn at it. Bloom saves the
 * zoom as a user setting, so a test that changes it should put it back when it is done.
 */
export async function setZoom(page: Page, percent: number): Promise<void> {
    const info = await getZoom(page);
    if (!info.zoomEnabled)
        throw new Error(
            "Bloom is not showing the Edit tab, so there is no page to zoom.",
        );
    if (percent < info.minZoom || percent > info.maxZoom)
        throw new Error(
            `Bloom's zoom goes from ${info.minZoom}% to ${info.maxZoom}%, so it cannot be set to ${percent}%.`,
        );
    await apiPost(
        page,
        "workspace/topRight/zoom",
        JSON.stringify({ zoom: percent }),
        "application/json",
    );
    await expect
        .poll(async () => (await getZoom(page)).zoom, {
            timeout: 30000,
            message: `Bloom never reported the zoom as ${percent}%.`,
        })
        .toBe(percent);
    // Bloom draws the zoom by scaling a container it wraps around the page (see SetupPageZoom in
    // EditingModel.cs and setZoom in workspaceRoot.ts). Wait for that to show the new zoom, or a
    // measurement taken right after this returns would see the old size.
    await expect
        .poll(
            async () =>
                editablePageFrame(page)
                    .locator("#page-scaling-container")
                    .evaluate((container) => {
                        const match = /scale\(([\d.]+)\)/.exec(
                            (container as HTMLElement).style.transform,
                        );
                        return match ? Math.round(Number(match[1]) * 100) : 0;
                    }),
            {
                timeout: 30000,
                message: `The page being edited was never drawn at ${percent}%.`,
            },
        )
        .toBe(percent);
}
