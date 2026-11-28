// Drive and read Bloom's top bar: the workspace tabs, and the zoom control at its right end.
//
// The WinForms shell, not the React top bar, owns which tab is active. So a test clicks the real
// button (switchTab) but asks Bloom's own workspace/tabs API what happened (getTabs,
// waitForActiveTab) instead of inferring it from the DOM. That is the assertion half of the
// UI-vs-API policy in README.md, and it is also the only way to know that the WinForms side has
// finished the switch rather than merely started it.

import { expect, type Page, type Response } from "@playwright/test";
import { apiGetJson, apiPost, getUiString } from "./api";
import { editablePageFrame, waitForBloomToHavePageAsItIs } from "./bookMaking";
import { pageListFrame } from "./pageList";
import { toolboxFrame } from "./toolbox";

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
 *
 * Leaving the Edit tab saves the page being edited, so this first waits until Bloom has that page
 * as it is now (waitForBloomToHavePageAsItIs; it returns at once when no page is being edited).
 */
export async function switchTab(
    page: Page,
    tab: WorkspaceTabId,
    timeoutMs = 30000,
): Promise<void> {
    await clickTab(page, tab, timeoutMs);
    await waitForActiveTab(page, tab, timeoutMs);
}

/**
 * The first half of switchTab: click a workspace tab, without asking Bloom whether the switch is
 * done. For a switch that raises a problem dialog (doThroughOneProblemDialog says why); call
 * waitForActiveTab once the dialog has closed.
 */
export async function clickTab(
    page: Page,
    tab: WorkspaceTabId,
    timeoutMs = 30000,
): Promise<void> {
    await waitForBloomToHavePageAsItIs(page);
    const target = page.getByTestId(tabTestId(tab));
    await target.waitFor({ state: "visible", timeout: timeoutMs });
    await target.click();
}

/**
 * Open the top bar's help menu (the "?" at its right end) and choose "Help...", which asks Bloom
 * to open its help file at the topic for the tab showing. The entry is found by its label in the
 * UI's language (HelpMenu.DocumentationMenuItem); the menu closes as the entry is chosen.
 */
export async function chooseHelpFromHelpMenu(page: Page): Promise<void> {
    const label = await getUiString(
        page,
        "HelpMenu.DocumentationMenuItem",
        "Help...",
    );
    await page.locator("#helpMenuButton").click();
    const entry = page.getByRole("menuitem", { name: label, exact: true });
    await entry.click();
    await expect(entry, "the help menu stayed open after Help...").toBeHidden();
}

/**
 * Open the top bar's help menu and choose "Report a Problem...", which asks Bloom to show its
 * report dialog (waitForReportDialogPage in problemDialogs.ts finds it). The entry is found by its
 * label in the UI's language (HelpMenu.ReportAProblemToolStripMenuItem).
 */
export async function chooseReportAProblemFromHelpMenu(
    page: Page,
): Promise<void> {
    const label = await getUiString(
        page,
        "HelpMenu.ReportAProblemToolStripMenuItem",
        "Report a Problem...",
    );
    await page.locator("#helpMenuButton").click();
    const entry = page.getByRole("menuitem", { name: label, exact: true });
    await entry.click();
    await expect(
        entry,
        "the help menu stayed open after Report a Problem...",
    ).toBeHidden();
}

/** What GET workspace/zoom replies with: the Edit tab's zoom, as a percentage. */
export interface IZoomInfo {
    zoom: number;
    minZoom: number;
    maxZoom: number;
    /** False outside the Edit tab, where the top bar hides the zoom control. */
    zoomEnabled: boolean;
}

/** Ask Bloom what the Edit tab's zoom is, and the range it allows. */
export async function getZoom(page: Page): Promise<IZoomInfo> {
    return apiGetJson<IZoomInfo>(page, "workspace/zoom");
}

/** The top bar's − ("out") or + ("in") zoom button. */
function zoomButton(page: Page, direction: "in" | "out") {
    // The − is an en dash in the top bar.
    return page.getByRole("button", {
        name: direction === "in" ? "+" : "–",
        exact: true,
    });
}

/**
 * Click the top bar's − ("out") or + ("in") zoom button once, as a person does, and return once
 * Bloom reports a different zoom. Returns the new zoom. Throws when the zoom is already at that end
 * of its range.
 */
export async function clickZoomButton(
    page: Page,
    direction: "in" | "out",
): Promise<number> {
    const before = await getZoom(page);
    if (
        direction === "in"
            ? before.zoom >= before.maxZoom
            : before.zoom <= before.minZoom
    )
        throw new Error(
            `The zoom is already ${before.zoom}%, the ${direction === "in" ? "largest" : "smallest"} Bloom allows.`,
        );
    await zoomButton(page, direction).click();
    await expect
        .poll(async () => (await getZoom(page)).zoom, {
            timeout: 5000,
            message: `Clicking the zoom ${direction} button did not change the zoom.`,
        })
        .not.toBe(before.zoom);
    return (await getZoom(page)).zoom;
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
        "workspace/zoom",
        JSON.stringify({ zoom: percent }),
        "application/json",
    );
    await expect
        .poll(async () => (await getZoom(page)).zoom, {
            timeout: 5000,
            message: `Bloom never reported the zoom as ${percent}%.`,
        })
        .toBe(percent);
    // A measurement taken right after this returns would otherwise see the old size.
    await expectPageDrawnAtZoom(page, percent);
}

/**
 * Click the top bar's − ("out") or + ("in") zoom button once while the zoom is already at that end
 * of its range, as a person does who keeps clicking, and return the zoom Bloom then reports. Returns
 * once Bloom has answered the request that the click sends, since a click that should change
 * nothing leaves no change to wait for. Throws when the zoom is not at that end of its range.
 */
export async function clickZoomButtonAtEndOfRange(
    page: Page,
    direction: "in" | "out",
): Promise<number> {
    const before = await getZoom(page);
    const end = direction === "in" ? before.maxZoom : before.minZoom;
    if (before.zoom !== end)
        throw new Error(
            `The zoom is ${before.zoom}%, not ${end}%, the ${direction === "in" ? "largest" : "smallest"} Bloom allows. Use clickZoomButton.`,
        );
    // Awaited together, so that a click that fails does not leave the wait to time out unhandled.
    await Promise.all([
        page.waitForResponse(
            (response) =>
                response.request().method() === "POST" &&
                new URL(response.url()).pathname.endsWith("/workspace/zoom"),
            { timeout: 30000 },
        ),
        zoomButton(page, direction).click(),
    ]);
    return (await getZoom(page)).zoom;
}

/** The zoom that the top bar shows between its − and + buttons, as a percentage. */
export async function getZoomShownInTopBar(page: Page): Promise<number> {
    const text = await zoomButton(page, "in")
        .locator("xpath=preceding-sibling::span[1]")
        .innerText();
    const match = /^(\d+)%$/.exec(text.trim());
    if (!match)
        throw new Error(
            `The top bar shows "${text}" between its zoom buttons, which is not a percentage.`,
        );
    return Number(match[1]);
}

/**
 * Hold Ctrl and turn the mouse wheel over the page being edited, `notches` notches: a positive
 * number turns it away from the user, which zooms in, and a negative number zooms out. The notches
 * go out as fast as a quick spin of the wheel. Returns the zoom Bloom reports once it has answered
 * for every notch.
 *
 * The page sends the notches of a quick spin in fewer requests than notches (setupWheelZooming in
 * bloomEditing.ts), and a notch at the end of the zoom's range changes nothing, so this counts the
 * notches in the requests Bloom has answered rather than waiting for a zoom.
 */
export async function turnWheelToZoom(
    page: Page,
    notches: number,
): Promise<number> {
    if (notches === 0 || !Number.isInteger(notches))
        throw new Error(
            `Turn the wheel a whole number of notches, not ${notches}.`,
        );
    let answeredNotches = 0;
    const countAnswered = (response: Response) => {
        const match = /\/pageControls\/zoomBy\?notches=(-?\d+)/.exec(
            response.url(),
        );
        if (match && response.ok()) answeredNotches += Number(match[1]);
    };
    page.on("response", countAnswered);
    try {
        // Near the top left corner of the page, which stays in view at any zoom.
        await editablePageFrame(page)
            .locator(".bloom-page")
            .first()
            .hover({ position: { x: 30, y: 30 } });
        await page.keyboard.down("Control");
        try {
            for (let i = 0; i < Math.abs(notches); i++)
                await page.mouse.wheel(0, notches > 0 ? -100 : 100);
        } finally {
            await page.keyboard.up("Control");
        }
        await expect
            .poll(() => answeredNotches, {
                timeout: 15000,
                message: `Bloom did not answer for ${notches} notches of Ctrl+mouse wheel over the page being edited.`,
            })
            .toBe(notches);
    } finally {
        page.off("response", countAnswered);
    }
    return (await getZoom(page)).zoom;
}

/**
 * Wait until the page being edited is drawn at `percent`. Bloom draws the zoom by scaling a
 * container it wraps around the page (see SetupPageZoom in EditingModel.cs and setZoom in
 * workspaceRoot.ts), a moment after it reports the new zoom.
 */
export async function expectPageDrawnAtZoom(
    page: Page,
    percent: number,
): Promise<void> {
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

/** The width and height on screen of the page being edited, in the page frame's pixels. */
export async function getSizeOfPageBeingEdited(
    page: Page,
): Promise<{ width: number; height: number }> {
    return editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .evaluate((element) => {
            const rect = element.getBoundingClientRect();
            return { width: rect.width, height: rect.height };
        });
}

/** How large one part of the Edit tab that the zoom leaves alone is drawn. */
export interface IUnzoomedPart {
    /** The width of the part's document, in its own pixels. */
    viewportWidth: number;
    /** How many device pixels one of its pixels takes, which a zoom of the browser changes. */
    pixelRatio: number;
    /** The width and height of one thing in it: a tab, the first page thumbnail, the tool list. */
    landmark: { width: number; height: number };
    /**
     * The width and height on screen of the whole part: the top bar itself, or the frame the page
     * list or the toolbox is drawn in, which scaling the frame changes while everything measured
     * inside it stays the same.
     */
    onScreen: { width: number; height: number };
}

/**
 * How large the parts of the Edit tab around the page being edited are drawn: the top bar, the
 * page list and the toolbox. The zoom is for the page alone, so a test reads this before and after
 * a zoom and expects no difference.
 */
export async function getSizesOfUnzoomedParts(page: Page): Promise<{
    topBar: IUnzoomedPart;
    pageList: IUnzoomedPart;
    toolbox: IUnzoomedPart;
}> {
    const measure = (landmark: Element) => {
        const rect = landmark.getBoundingClientRect();
        return {
            viewportWidth: window.innerWidth,
            pixelRatio: window.devicePixelRatio,
            landmark: { width: rect.width, height: rect.height },
        };
    };
    const sizeOnScreen = (element: Element) => {
        const rect = element.getBoundingClientRect();
        return { width: rect.width, height: rect.height };
    };
    const pageList = pageListFrame(page);
    const toolbox = toolboxFrame(page);
    return {
        topBar: {
            ...(await page.getByTestId(tabTestId("edit")).evaluate(measure)),
            onScreen: await page
                .getByTestId("workspace-top-bar")
                .evaluate(sizeOnScreen),
        },
        pageList: {
            ...(await pageList.locator(".gridItem").first().evaluate(measure)),
            onScreen: await (
                await pageList.frameElement()
            ).evaluate(sizeOnScreen),
        },
        toolbox: {
            ...(await toolbox.locator("body").evaluate(measure)),
            onScreen: await (
                await toolbox.frameElement()
            ).evaluate(sizeOnScreen),
        },
    };
}
