// The game tabs above a game page (Start, Correct, Wrong, Play) in the bar of controls across the
// top of the Edit tab's page frame, with one page or all pages showing, and in a narrow window
// (BL-16929). That bar is fixed across the top of the page frame in both views, so the tabs must
// stay in it, on one line, clear of the page, and still work.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    clickGameTab,
    getGameTabPlacements,
    kGameTabs,
} from "../helpers/games";
import { expectInside, expectNoOverlap, type IRect } from "../helpers/geometry";
import {
    choosePageView,
    getPagePlacement,
    type IViewRect,
} from "../helpers/pageView";
import {
    getWindowSize,
    resizeWindow,
    restoreWindowSize,
} from "../helpers/windowSize";

// The Games template's pages are subscription-gated.
test.use({
    collectionSpec: {
        name: "all-pages-game-tabs",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const asRect = (r: IViewRect): IRect => ({
    x: r.left,
    y: r.top,
    width: r.right - r.left,
    height: r.bottom - r.top,
});

test("on two kinds of game page, in both views and in a narrow window, the game tabs stay on one line in the bar above the page and still work", async ({
    page,
}) => {
    test.setTimeout(600000);
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Match Word with Image", 1, "Games");
    await addPage(page, "Order Words", 1, "Games");
    const games = await getContentPages(page);
    const { width, height } = await getWindowSize(page);

    for (const view of ["one", "all"] as const)
        for (const [g, game] of games.entries())
            for (const narrow of [false, true]) {
                const where = `${view === "all" ? "All Pages" : "One Page"}, game ${g + 1}${narrow ? ", narrow window" : ""}`;
                await goToPage(page, game.id);
                await choosePageView(page, view);
                await waitForEditTabSettled(page);

                // THE ACTION UNDER TEST: a narrow window (and the views and kinds of game).
                if (narrow) await resizeWindow(page, { width: 640, height });
                else await resizeWindow(page, { width, height });

                const { bar, tabs } = await getGameTabPlacements(page);
                for (const tab of kGameTabs)
                    expectInside(
                        asRect(tabs[tab]),
                        asRect(bar),
                        `${where}: the ${tab} tab`,
                        "the bar above the page",
                    );
                for (const tab of kGameTabs)
                    expect(
                        Math.abs(tabs[tab].top - tabs.Start.top),
                        `${where}: the ${tab} tab is not on the same line as Start.`,
                    ).toBeLessThan(2);
                expectNoOverlap(
                    kGameTabs.map((tab) => ({
                        name: tab,
                        rect: asRect(tabs[tab]),
                    })),
                    `${where}: the game tabs`,
                );
                const placement = await getPagePlacement(page, game.id);
                expect(
                    bar.bottom,
                    `${where}: the bar of controls covers the top of the page.`,
                ).toBeLessThanOrEqual(placement.page.top + 1);

                // The tabs still work.
                await clickGameTab(page, "Correct");
                await clickGameTab(page, "Wrong");
                await clickGameTab(page, "Start");
            }
    await restoreWindowSize(page);
});
