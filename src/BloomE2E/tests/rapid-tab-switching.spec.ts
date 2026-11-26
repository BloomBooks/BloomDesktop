// Flipping fast between the Collections, Edit and Publish tabs must not break Bloom.
//
// A tester used to try "various combinations" of fast tab switches by hand. This clicks the real
// top-bar tabs in bursts, one click straight after the other, without waiting for Bloom to finish
// a switch before the next click. After each burst it waits for Bloom to settle and checks that:
//
// - Bloom ends up on the last tab it was asked for, and the top bar shows that same tab;
// - no tab stays greyed out (the case allows them to be greyed for a moment during the switches);
// - that tab shows what it should: the page being edited, the publish destinations, or the
//   selected book's preview;
// - no script error and no "Bloom had a problem" dialog came up (the fixture fails a test on the
//   dialog by itself).
//
// The first burst starts in the Edit tab straight after typing, so leaving Edit mid-burst has to
// save the page; the test then checks that the typing is in the book file.

import * as fs from "node:fs";
import type { Page, Request } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForBloomToHaveTyping,
    waitForEditablePage,
} from "../helpers/bookMaking";
import { bookHtmlPath, readBook } from "../helpers/bookHtml";
import {
    collectionBookPreview,
    getSelectedBookId,
} from "../helpers/collection";
import { getPublishDestinationsOffered } from "../helpers/publish";
import { watchForScriptErrors } from "../helpers/scriptErrors";
import { getTabs, switchTab, type WorkspaceTabId } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "rapid-tab-switching", languages: ["en"] },
});

const kAllTabs: WorkspaceTabId[] = ["collection", "edit", "publish"];

// Each burst is clicked from whatever tab the previous one left Bloom on. Between them they go
// from every tab to every other, double back, and click the same tab twice running.
const kBursts: WorkspaceTabId[][] = [
    ["collection", "publish", "edit", "collection", "edit", "publish"],
    ["edit", "collection", "publish", "publish", "edit", "collection"],
    ["publish", "edit", "publish", "collection", "edit", "edit"],
    ["collection", "edit", "collection", "publish", "collection", "publish"],
];

/** Keeps the workspace/selectTab requests the top bar sends, and which of them are still open. */
function watchTabRequests(page: Page) {
    const asked: WorkspaceTabId[] = [];
    const open = new Set<Request>();
    const isSelectTab = (request: Request) =>
        new URL(request.url()).pathname.endsWith("/workspace/selectTab");
    const onRequest = (request: Request) => {
        if (!isSelectTab(request)) return;
        asked.push(JSON.parse(request.postData() ?? "{}").tab);
        open.add(request);
    };
    const onDone = (request: Request) => {
        open.delete(request);
    };
    page.on("request", onRequest);
    page.on("requestfinished", onDone);
    page.on("requestfailed", onDone);
    return {
        asked: () => [...asked],
        openCount: () => open.size,
        stop: () => {
            page.off("request", onRequest);
            page.off("requestfinished", onDone);
            page.off("requestfailed", onDone);
        },
    };
}

/** The tab the top bar draws as the selected one, by its API name, or "" when none. */
async function tabShownSelected(page: Page): Promise<string> {
    const selected = page.locator(
        '[data-testid^="workspace-tab-"][aria-selected="true"]',
    );
    if ((await selected.count()) !== 1) return "";
    return ((await selected.getAttribute("data-testid")) ?? "").replace(
        "workspace-tab-",
        "",
    );
}

/** Click the real top-bar tabs in order, each click straight after the one before. */
async function clickTabsInABurst(page: Page, tabs: WorkspaceTabId[]) {
    // Measure first, so nothing between two clicks waits on the page. The tabs do not move while
    // a book is selected: all three are shown, at the left of the top bar.
    const centres = new Map<WorkspaceTabId, { x: number; y: number }>();
    for (const tab of kAllTabs) {
        const box = await page
            .getByTestId(`workspace-tab-${tab}`)
            .boundingBox();
        if (!box)
            throw new Error(`The ${tab} tab is not showing in the top bar.`);
        centres.set(tab, {
            x: box.x + box.width / 2,
            y: box.y + box.height / 2,
        });
    }
    for (const tab of tabs) {
        const { x, y } = centres.get(tab)!;
        await page.mouse.click(x, y);
    }
}

/**
 * Wait until Bloom has finished every switch it was asked for, and check that it settled on the
 * last one, with the top bar agreeing and no tab left greyed out.
 */
async function expectSettledOn(
    page: Page,
    requests: ReturnType<typeof watchTabRequests>,
    tab: WorkspaceTabId,
) {
    await expect
        .poll(() => requests.openCount(), {
            timeout: 60000,
            message: "Bloom never answered every request to change tabs.",
        })
        .toBe(0);
    await expect
        .poll(
            async () => {
                const { tabStates, navigationLocked } = await getTabs(page);
                return {
                    // Only the three tabs: Bloom's reply carries other fields beside them.
                    tabStates: Object.fromEntries(
                        kAllTabs.map((t) => [t, tabStates[t]]),
                    ),
                    navigationLocked,
                    shownSelected: await tabShownSelected(page),
                };
            },
            {
                timeout: 30000,
                message: `Bloom did not settle on the ${tab} tab, the last one it was asked for, with every tab available again.`,
            },
        )
        .toEqual({
            tabStates: Object.fromEntries(
                kAllTabs.map((t) => [t, t === tab ? "active" : "enabled"]),
            ),
            navigationLocked: false,
            shownSelected: tab,
        });
}

/** Check that the tab now showing shows its content, not a blank or half-built screen. */
async function expectTabShowsItsContent(
    page: Page,
    tab: WorkspaceTabId,
    bookId: string,
    typed: string,
) {
    switch (tab) {
        case "edit":
            await waitForEditablePage(page);
            await expect(
                editablePageFrame(page).locator(".bloom-page"),
                "the Edit tab should show the page that was being edited",
            ).toContainText(typed, { timeout: 30000 });
            break;
        case "publish":
            await expect
                .poll(
                    async () =>
                        (await getPublishDestinationsOffered(page)).length,
                    {
                        timeout: 60000,
                        message:
                            "The Publish tab never offered anywhere to publish to.",
                    },
                )
                .toBeGreaterThan(0);
            break;
        case "collection":
            expect(
                await getSelectedBookId(page),
                "the Collections tab should still have the book selected",
            ).toBe(bookId);
            await expect(
                collectionBookPreview(page).locator(".bloom-page").first(),
                "the Collections tab should preview the selected book",
            ).toBeVisible({ timeout: 60000 });
            break;
    }
}

test("flipping fast between the Collections, Edit and Publish tabs [Test Case ID 102]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);
    const typed = "typed just before the tabs flipped";

    const made = await step("Make a book with a page of text", async () => {
        const folder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        const [contentPage] = await getContentPages(page);
        await goToPage(page, contentPage.id);
        await typeInGroup(page, ".bloom-translationGroup", "en", typed);
        await waitForBloomToHaveTyping(page, typed);
        const id = await getSelectedBookId(page);
        if (!id) throw new Error("test setup: Bloom has no book selected");
        return { bookFolder: folder, bookId: id };
    });
    if (!made) throw new Error("test setup: the book was not made");
    const { bookFolder, bookId } = made;
    // Sanity check: the burst below starts by leaving Edit, which is what has to save the typing.
    expect((await getTabs(page)).tabStates.edit, "test setup").toBe("active");
    expect(
        fs.readFileSync(bookHtmlPath(bookFolder), "utf8"),
        "test setup: the typing must not be in the file before Edit is left",
    ).not.toContain(typed);

    const scriptErrors = watchForScriptErrors(page);
    const requests = watchTabRequests(page);
    try {
        for (const [index, burst] of kBursts.entries()) {
            await step(
                `Flip the tabs fast: ${burst.join(", ")} (burst ${index + 1} of ${kBursts.length})`,
                async () => {
                    const askedBefore = requests.asked().length;
                    await clickTabsInABurst(page, burst);
                    // The top bar ignores a click on the tab already showing or on a greyed one,
                    // so which clicks became requests depends on timing. Bloom should end on the
                    // last tab it was asked for, whichever that was. Sanity check: at least two
                    // switches were asked for, so they overlapped rather than ran one by one.
                    await expect
                        .poll(() => requests.asked().length, {
                            timeout: 10000,
                            message:
                                "The burst of clicks sent Bloom fewer than two requests to change tabs.",
                        })
                        .toBeGreaterThanOrEqual(askedBefore + 2);
                    const settledOn = requests.asked().at(-1)!;
                    await expectSettledOn(page, requests, settledOn);
                    await expectTabShowsItsContent(
                        page,
                        settledOn,
                        bookId,
                        typed,
                    );
                },
            );
        }

        await step(
            "Check the typing was saved when Edit was left",
            async () => {
                const saved = await readBook(page, bookFolder);
                expect(
                    saved.pages.map((p) => p.text).join("\n"),
                    "the book file should hold what was typed just before the tabs flipped",
                ).toContain(typed);
            },
        );

        await step("Go to each tab once more, the ordinary way", async () => {
            const current = (requests.asked().at(-1) ??
                "edit") as WorkspaceTabId;
            const others = kAllTabs.filter((t) => t !== current);
            for (const tab of [...others, current]) {
                await switchTab(page, tab);
                await expectTabShowsItsContent(page, tab, bookId, typed);
            }
        });

        expect(
            scriptErrors.errors(),
            "Bloom's UI should report no script error while the tabs flip",
        ).toEqual([]);
    } finally {
        requests.stop();
        scriptErrors.stop();
    }
});
