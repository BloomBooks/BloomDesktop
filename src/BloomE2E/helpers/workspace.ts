// Drive and read Bloom's top bar: the workspace tabs, and the zoom control at its right end.
//
// The WinForms shell, not the React top bar, owns which tab is active. So a test clicks the real
// button (switchTab) but asks Bloom's own workspace/tabs API what happened (getTabs,
// waitForActiveTab) instead of inferring it from the DOM. That is the assertion half of the
// UI-vs-API policy in README.md, and it is also the only way to know that the WinForms side has
// finished the switch rather than merely started it.

import { expect, type Page } from "@playwright/test";
import { apiGet, apiGetJson, apiPost } from "./api";
import {
    editablePageFrame,
    getShownPageId,
    msUntilEditing,
    waitForEditTabSettled,
} from "./bookMaking";

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
            timeout: 5000,
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
                timeout: 5000,
                message: `The page being edited was never drawn at ${percent}%.`,
            },
        )
        .toBe(percent);
}

/** Whether Bloom's "Simulate a slow computer" testing mode is on (see SlowComputerSimulation.cs). */
export async function isSlowComputerSimulationOn(page: Page): Promise<boolean> {
    return (await apiGet(page, "app/simulateSlowComputer")).body === "true";
}

/**
 * Turn the "Simulate a slow computer" testing mode on or off the way a tester does: right-click an
 * empty part of the top bar, which opens the testing menu, and click the item. Returns once Bloom
 * reports the mode in the state asked for. Does nothing when it is already in that state.
 *
 * The mode lasts for the rest of this Bloom's run, and every test in a spec file shares one Bloom,
 * so a test that turns it on should turn it off again before it ends.
 */
export async function setSlowComputerSimulation(
    page: Page,
    on: boolean,
): Promise<void> {
    if ((await isSlowComputerSimulationOn(page)) === on) return;
    // The testing menu opens on a right-click anywhere on the top bar that is not a control. Just
    // to the right of the tabs is a spacer (see TopBar.tsx), so click there.
    const topBar = page.getByTestId("workspace-top-bar");
    const topBarBox = await topBar.boundingBox();
    const tabsBox = await page.getByTestId("workspace-tabs").boundingBox();
    if (!topBarBox || !tabsBox)
        throw new Error(
            "The top bar or its tabs have no on-screen box, so there is nowhere to right-click.",
        );
    await topBar.click({
        button: "right",
        position: {
            x: tabsBox.x + tabsBox.width - topBarBox.x + 8,
            y: tabsBox.y + tabsBox.height / 2 - topBarBox.y,
        },
    });
    // The item's label is not localized: this whole menu is for developers and testers.
    const item = page.getByRole("menuitem", {
        name: "Simulate a slow computer",
    });
    await item.waitFor({ state: "visible", timeout: 15000 });
    await item.click();
    await expect
        .poll(() => isSlowComputerSimulationOn(page), {
            timeout: 15000,
            message: `Bloom never reported "Simulate a slow computer" as ${on ? "on" : "off"}.`,
        })
        .toBe(on);
}

/**
 * Leave the Edit tab for the Collections tab, come back by clicking the Edit tab, and return how
 * long coming back took: from the click until the Edit tab was editing its page again (see
 * msUntilEditing). The Edit tab is settled when this returns, but that last wait is not part of
 * the time. The Edit tab must be showing a page when this is called.
 */
export async function returnToEditTabTimed(page: Page): Promise<number> {
    const pageId = await getShownPageId(page);
    if (!pageId)
        throw new Error(
            "The Edit tab is not showing a page, so there is nothing to come back to.",
        );
    await switchTab(page, "collection");
    const editTab = page.getByTestId(tabTestId("edit"));
    await editTab.waitFor({ state: "visible", timeout: 30000 });
    const startedAt = performance.now();
    await editTab.click();
    const ms = await msUntilEditing(page, pageId, startedAt);
    await waitForEditTabSettled(page);
    return ms;
}
