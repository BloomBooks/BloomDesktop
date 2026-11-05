// A page in the Edit tab takes no typing until it is ready to use, and keeps what is typed after.
//
// A page shows, with its text boxes editable, before Bloom has finished setting it up; when each
// text box's editor finishes starting, it writes back the text it found, wiping out anything typed
// since. So until the page says it is ready, Bloom ignores the user's keys (see pageInputBlock.ts
// and coverPageUntilReadyForInput in workspaceRoot.ts, in BloomBrowserUI).
//
// On a developer's machine that window is well under a second, so these tests slow Bloom's web
// side five times with the DevTools CPU slowdown, which stretches it to a few seconds, about what
// users with old machines see. They type straight after changing pages until the page is ready,
// then check that none of the text typed while it was getting ready is on the page, and that what
// is typed afterwards is kept.
//
// The tests are serial because each one works on the book the first one builds.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditablePage,
    type IBookPage,
} from "../helpers/bookMaking";
import { pageListFrame } from "../helpers/pageThumbnails";
import {
    choosePageView,
    clickPageInGrid,
    getPagePlacement,
    isPageChangeUnderway,
    waitForClickedPageToSettle,
} from "../helpers/pageView";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "page-takes-input-when-ready", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

let pages: IBookPage[];

/**
 * Run `action` with Bloom's web side slowed five times, as on an old machine. The slowdown lasts
 * only as long as the DevTools session that set it.
 */
async function whileSlowedDown(page: Page, action: () => Promise<void>) {
    const session = await page.context().newCDPSession(page);
    await session.send("Emulation.setCPUThrottlingRate", { rate: 5 });
    try {
        await action();
    } finally {
        await session.send("Emulation.setCPUThrottlingRate", { rate: 1 });
        await session.detach();
    }
}

/** Click a page's thumbnail in the page list, without waiting for the page to load. */
async function clickThumbnail(page: Page, pageId: string) {
    await pageListFrame(page)
        .locator(
            `#pageGridWrapper .gridItem[id="${pageId}"] .invisibleThumbnailCover`,
        )
        .click();
}

/**
 * Once the page change has started, type while it is under way, and check after each word that
 * it has not appeared on the page. (Checking only once the page is ready would not do: the text
 * box's editor, as it finishes starting, wipes out what was typed, so the words would be gone by
 * then even if they had got in.) Returns the words that certainly went in before the new page was
 * ready: the last one, which may have gone in after, is left out.
 */
async function typeWhileThePageGetsReady(page: Page): Promise<string[]> {
    // The page list asks Bloom for the change, which comes back a moment later; typing before
    // that would rightly go into the page being left.
    await expect
        .poll(() => isPageChangeUnderway(page), {
            message: "The page change never started.",
        })
        .toBe(true);
    const early: string[] = [];
    for (let i = 1; ; i++) {
        const word = `early${i}`;
        await page.keyboard.type(`${word} `);
        const text = await textOfPageNow(page);
        if (!(await isPageChangeUnderway(page))) break;
        expect(
            text,
            `"${word}", typed before the page was ready, got onto the page.`,
        ).not.toContain(word);
        early.push(word);
    }
    expect(
        early.length,
        "test setup: the page was ready before any typing finished, so the test checked nothing; Bloom needs slowing down more",
    ).toBeGreaterThan(0);
    return early;
}

/**
 * The text of whatever page the page frame holds at this moment, without waiting for one: while
 * the page is changing there may be none, or the frame may be between documents.
 */
async function textOfPageNow(page: Page): Promise<string> {
    const frame = page.frame({ name: "page" });
    if (!frame) return "";
    return frame
        .evaluate(
            () => document.querySelector(".bloom-page")?.textContent ?? "",
        )
        .catch(() => "");
}

/** The text of the page being edited. */
async function textOfPage(page: Page): Promise<string> {
    return editablePageFrame(page).locator(".bloom-page").first().innerText();
}

/** Check that none of `words` is on the page being edited. */
async function expectNoneOnPage(page: Page, words: string[], when: string) {
    const text = await textOfPage(page);
    for (const word of words)
        expect(
            text,
            `"${word}", typed before the page was ready, got onto the page (${when}).`,
        ).not.toContain(word);
}

test.describe("a page takes input only when it is ready", () => {
    test("builds a book with three content pages", async ({ page }) => {
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 3);
        pages = await getContentPages(page);
        expect(pages, "test setup: three content pages").toHaveLength(3);
        await goToPage(page, pages[0].id);
    });

    test("changing pages in the page list", async ({ page }) => {
        const target = pages[1];
        let early: string[] = [];
        await whileSlowedDown(page, async () => {
            await clickThumbnail(page, target.id);
            early = await typeWhileThePageGetsReady(page);
            await waitForEditablePage(page);
        });
        expect(await getShownPageId(page)).toBe(target.id);
        await expectNoneOnPage(page, early, "just after it became ready");

        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "en",
            "typed once ready",
        );
        await goToPage(page, pages[2].id);
        await goToPage(page, target.id);
        expect(await textOfPage(page)).toContain("typed once ready");
        await expectNoneOnPage(page, early, "after leaving and coming back");
    });

    test("clicking the facing page with all pages showing", async ({
        page,
    }) => {
        await goToPage(page, pages[0].id);
        await choosePageView(page, "all");
        try {
            // Click the middle of the part of the next page that is in view.
            const target = pages[2];
            await goToPage(page, pages[1].id);
            const placement = await getPagePlacement(page, target.id);
            const top = Math.max(placement.page.top, placement.visible.top);
            const bottom = Math.min(
                placement.page.bottom,
                placement.visible.bottom,
            );
            expect(
                bottom - top,
                "test setup: the page to click is not in view",
            ).toBeGreaterThan(20);
            const yInView = Math.round(
                (top + bottom) / 2 - placement.visible.top,
            );
            let early: string[] = [];
            await whileSlowedDown(page, async () => {
                await clickPageInGrid(page, target.id, yInView);
                early = await typeWhileThePageGetsReady(page);
                await waitForEditablePage(page);
            });
            await waitForClickedPageToSettle(page);
            expect(await getShownPageId(page)).toBe(target.id);
            await expectNoneOnPage(page, early, "just after it became ready");

            await typeInGroup(
                page,
                ".bloom-translationGroup",
                "en",
                "typed once ready in all pages",
            );
            await goToPage(page, pages[0].id);
            await goToPage(page, target.id);
            expect(await textOfPage(page)).toContain(
                "typed once ready in all pages",
            );
            await expectNoneOnPage(
                page,
                early,
                "after leaving and coming back",
            );
        } finally {
            await choosePageView(page, "one");
        }
    });
});
