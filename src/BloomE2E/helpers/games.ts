// The game tabs (Start, Correct, Wrong, Play) that a game page shows in the bar of controls above
// the page, in the Edit tab's page frame (DragActivityTabControl.tsx). Choosing a tab shows the page
// as it looks in that state of the game.

import { expect, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import type { IViewRect } from "./pageView";

export type GameTab = "Start" | "Correct" | "Wrong" | "Play";
export const kGameTabs: GameTab[] = ["Start", "Correct", "Wrong", "Play"];

function gameTabControl(page: Page): Locator {
    return editablePageFrame(page).locator("#drag-activity-tab-control");
}

function gameTab(page: Page, tab: GameTab): Locator {
    return gameTabControl(page).getByRole("button", { name: tab, exact: true });
}

/**
 * Where each game tab is, and the bar of controls across the top of the page frame that holds
 * them, in the page frame's view. Waits for the tabs to show.
 */
export async function getGameTabPlacements(page: Page): Promise<{
    bar: IViewRect;
    tabs: Record<GameTab, IViewRect>;
}> {
    await expect(
        gameTab(page, "Play"),
        "The page being edited shows no game tabs.",
    ).toBeVisible({ timeout: 30000 });
    const rectOf = (l: Locator) =>
        l.evaluate((e) => {
            const r = e.getBoundingClientRect();
            return {
                left: r.left,
                top: r.top,
                right: r.right,
                bottom: r.bottom,
            };
        });
    const tabs = {} as Record<GameTab, IViewRect>;
    for (const tab of kGameTabs) tabs[tab] = await rectOf(gameTab(page, tab));
    const bar = await rectOf(
        editablePageFrame(page).locator("body > .bloom-controls-bar"),
    );
    return { bar, tabs };
}

/**
 * Which game tab the page being edited is showing: the tab the Game tool last set, which it keeps
 * on the Edit tab's own window (setActiveDragActivityTab in GameTool.tsx).
 */
export async function getGameTabShowing(
    page: Page,
): Promise<GameTab | undefined> {
    const index = await page.evaluate(
        () =>
            (window as unknown as Record<string, unknown>).dragActivityPage as
                | number
                | undefined,
    );
    return index === undefined ? undefined : kGameTabs[index];
}

/** Click a game tab with the mouse, and wait until the page shows that state of the game. */
export async function clickGameTab(page: Page, tab: GameTab): Promise<void> {
    await gameTab(page, tab).click();
    await expect
        .poll(() => getGameTabShowing(page), {
            timeout: 15000,
            message: `Clicking the ${tab} game tab did not show the page in that state.`,
        })
        .toBe(tab);
}
