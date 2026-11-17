// Typing, clicking and using a tool straight after a page change, with the Edit tab showing all the
// pages of the book and with one page at a time (BL-16929).
//
// With All Pages, clicking another page makes it the page being edited and replays the click on it,
// so the text cursor goes where the user clicked. The page appears before its text boxes' editors
// are ready, and the user may type, click elsewhere on the page, or click outside it in that
// moment. Each test here is one of those things.
//
// The tests are serial because each one starts from the book the one before it left behind.

import * as fs from "node:fs";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForBloomToHaveTyping,
    waitForEditablePage,
    waitForEditTabSettled,
    type IBookPage,
} from "../helpers/bookMaking";
import { bookHtmlPath, readBook } from "../helpers/bookHtml";
import {
    getStatusOfFocusedEditor,
    holdCkEditorStartup,
} from "../helpers/ckeditorStartup";
import { clickPageThumbnail } from "../helpers/pageList";
import {
    choosePageView,
    clickTextOfPageInGrid,
    expectGridToMatchBook,
    getFocusedGroupIndex,
    isPageChangeUnderway,
    textBoxOfOtherPage,
    waitForClickedPageToSettle,
    waitForFocusInGroup,
    waitForPageToBeShown,
} from "../helpers/pageView";
import {
    getNarrationSentences,
    openToolboxWithTalkingBook,
} from "../helpers/talkingBook";
import {
    clickToolHeader,
    getExpandedTool,
    hideToolbox,
} from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "all-pages-typing", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

let bookFolder: string;
// The six pages that each hold one text box, in the book's order.
let textPages: IBookPage[];
// A page with two text boxes, one above its picture and one below.
let twoBoxPage: IBookPage;

/** The text typed on the text page with this index when the book is built. */
const textOfPage = (index: number) => `Words on content page ${index + 1}`;

const kGroup = ".bloom-translationGroup";

/** The text of the first text box of the page being edited. */
async function textOfShownPage(page: Page, groupIndex = 0): Promise<string> {
    return (
        await editablePageFrame(page)
            .locator(kGroup)
            .nth(groupIndex)
            .locator('.bloom-editable[lang="en"]')
            .first()
            .innerText()
    ).trim();
}

/** The text the saved book holds for the page with this id. */
async function savedTextOfPage(page: Page, pageId: string): Promise<string> {
    return (
        (await readBook(page, bookFolder)).pages.find((p) => p.id === pageId)
            ?.text ?? "(no such page in the saved book)"
    );
}

test.describe("straight after a page change", () => {
    test("builds a book with six text pages and a page with two text boxes", async ({
        page,
    }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Just Text", 6);
        textPages = await getContentPages(page);
        expect(textPages.length).toBe(6);
        for (let i = 0; i < textPages.length; i++) {
            await goToPage(page, textPages[i].id);
            await typeInGroup(page, kGroup, "en", textOfPage(i));
        }
        // After the last text page, so that it faces none of the pages the tests type on.
        const before = await getPages(page);
        await addPage(page, "Image in Middle");
        twoBoxPage = (await getPages(page)).find(
            (p) => !before.some((b) => b.id === p.id),
        )!;
        await goToPage(page, twoBoxPage.id);
        await expect(
            editablePageFrame(page).locator(kGroup),
            "test setup: the page should have two text boxes",
        ).toHaveCount(2);
        await goToPage(page, textPages[0].id);
        await choosePageView(page, "all");
    });

    test("with All Pages, text typed as soon as the cursor is in the clicked page's text stays there, and none of it lands on the page left behind", async ({
        page,
    }) => {
        const leaving = textPages[0];
        const target = textPages[1];
        await goToPage(page, leaving.id);
        await expectGridToMatchBook(page);

        // THE ACTION UNDER TEST: a real click on the other page's text, and typing the moment the
        // cursor is there, without waiting for Bloom to finish setting the page up.
        await clickTextOfPageInGrid(page, target.id);
        await waitForPageToBeShown(page, target.id);
        await waitForFocusInGroup(page, 0);
        const typed = "TYPED AT ONCE";
        await page.keyboard.insertText(typed);

        await waitForClickedPageToSettle(page);
        await waitForEditablePage(page);
        await waitForEditTabSettled(page);
        // Everything typed is still there, in the middle of the text, which is where the click was.
        const text = await textOfShownPage(page);
        expect(text).toContain(typed);
        expect(text.replace(typed, "")).toBe(textOfPage(1));
        expect(
            text.startsWith(typed) || text.endsWith(typed),
            `The cursor should have been in the middle of the text, where the click was, but the page now reads "${text}".`,
        ).toBe(false);
        expect(await getFocusedGroupIndex(page)).toBe(0);
        // None of it is on the page that was left.
        await expect(await textBoxOfOtherPage(page, leaving.id)).toHaveText(
            textOfPage(0),
        );

        // And it is what Bloom saves.
        await waitForBloomToHaveTyping(page, typed);
        await goToPage(page, textPages[2].id);
        await expect
            .poll(() => savedTextOfPage(page, target.id))
            .toContain(typed);
        expect(await savedTextOfPage(page, leaving.id)).not.toContain(typed);
    });

    test("with All Pages, a click in a second text box before the clicked page is ready keeps the cursor and the typing in the second box", async ({
        page,
    }) => {
        await goToPage(page, textPages[5].id);
        await expectGridToMatchBook(page);
        const hold = await holdCkEditorStartup(page);

        // Open the page by clicking its first text box.
        await clickTextOfPageInGrid(page, twoBoxPage.id, 0);
        await waitForPageToBeShown(page, twoBoxPage.id);
        await hold.requested;

        // THE ACTION UNDER TEST: before the page's editors are ready, click the second text box
        // and type.
        await clickInGroup(page, kGroup, "en", 1);
        expect(
            await getStatusOfFocusedEditor(page),
            "test setup: the second box's editor should still be starting when it is clicked",
        ).not.toBe("ready");
        await page.keyboard.insertText("In the second box");

        await hold.release();
        await waitForClickedPageToSettle(page);
        await waitForEditTabSettled(page);
        // The click that opened the page did not take the cursor back to the first box.
        expect(await getFocusedGroupIndex(page)).toBe(1);
        await page.keyboard.insertText(", and more");
        expect(await textOfShownPage(page, 1)).toBe(
            "In the second box, and more",
        );
        expect(await textOfShownPage(page, 0)).toBe("");
    });

    test("with All Pages, a click in the toolbox before the clicked page is ready keeps the cursor out of the page", async ({
        page,
    }) => {
        await goToPage(page, textPages[2].id);
        await expectGridToMatchBook(page);
        await openToolboxWithTalkingBook(page);

        // First, what the click that opens a page does when the user does nothing more: once the
        // page's editors are ready, it puts the cursor in the text box that was clicked.
        let hold = await holdCkEditorStartup(page);
        await clickTextOfPageInGrid(page, textPages[3].id);
        await waitForPageToBeShown(page, textPages[3].id);
        await hold.requested;
        await hold.release();
        await waitForClickedPageToSettle(page);
        await waitForEditTabSettled(page);
        expect(
            await getFocusedGroupIndex(page),
            "test setup: left alone, the click that opened the page should put the cursor in its text box",
        ).toBe(0);

        const target = textPages[2];
        hold = await holdCkEditorStartup(page);
        await clickTextOfPageInGrid(page, target.id);
        await waitForPageToBeShown(page, target.id);
        await hold.requested;

        // THE ACTION UNDER TEST: before the page's editors are ready, click something outside the
        // page.
        await clickToolHeader(page, "talkingBook");

        await hold.release();
        await waitForClickedPageToSettle(page);
        await waitForEditTabSettled(page);
        expect(
            await getFocusedGroupIndex(page),
            "The click that opened the page took the cursor back into its text box after the user had clicked in the toolbox.",
        ).toBeUndefined();
        await hideToolbox(page);
    });

    for (const view of ["one", "all"] as const) {
        test(`clicking quickly through five pages in the page list ends on the last one, ready to type in (${view === "one" ? "One Page" : "All Pages"})`, async ({
            page,
        }) => {
            await goToPage(page, textPages[0].id);
            await choosePageView(page, view);

            // THE ACTION UNDER TEST: five clicks, none waiting for the page before it to show.
            for (const next of textPages.slice(1))
                await clickPageThumbnail(page, next.id);

            const last = textPages[5];
            await waitForPageToBeShown(page, last.id);
            await waitForEditablePage(page);
            await waitForEditTabSettled(page);
            expect(await getShownPageId(page)).toBe(last.id);
            expect(
                await isPageChangeUnderway(page),
                "The wait cursor is still over the pages after the last page showed.",
            ).toBe(false);
            if (view === "all") await expectGridToMatchBook(page);
            const typed = `Typed after quick clicks, ${view}`;
            await typeInGroup(page, kGroup, "en", typed);
            await goToPage(page, textPages[0].id);
            await expect
                .poll(() => savedTextOfPage(page, last.id))
                .toContain(typed);
        });
    }

    for (const { view, name } of [
        { view: "one", name: "One Page, from the page list" },
        { view: "all", name: "All Pages, by clicking the page" },
    ] as const) {
        test(`the Talking Book tool, clicked right after a page change, works on the new page (${name})`, async ({
            page,
        }) => {
            const target = textPages[4];
            await goToPage(page, textPages[3].id);
            await choosePageView(page, view);
            await openToolboxWithTalkingBook(page);

            // THE ACTION UNDER TEST: change pages, and click the tool as soon as the page shows.
            if (view === "one") await clickPageThumbnail(page, target.id);
            else await clickTextOfPageInGrid(page, target.id);
            await waitForPageToBeShown(page, target.id);
            await clickToolHeader(page, "talkingBook");

            // The tool has marked the new page's sentences for recording.
            await expect
                .poll(
                    async () =>
                        (await getNarrationSentences(page).catch(() => []))
                            .map((s) => s.text)
                            .join(" "),
                    {
                        timeout: 60000,
                        message:
                            "The Talking Book tool never marked the sentences of the new page.",
                    },
                )
                .toContain(textOfPage(4));
            expect(await getShownPageId(page)).toBe(target.id);
            expect(await getExpandedTool(page)).toBe("talkingBook");
            await waitForEditTabSettled(page);
            await hideToolbox(page);
        });
    }

    test("with All Pages, text typed on a page shows on it once another page is clicked, and the saved book has nothing from the other pages", async ({
        page,
    }) => {
        const typedOn = textPages[2];
        await goToPage(page, typedOn.id);
        await choosePageView(page, "all");
        const typed = "Typed before clicking another page";
        await typeInGroup(page, kGroup, "en", typed);

        // THE ACTION UNDER TEST: a real click on another page.
        await clickTextOfPageInGrid(page, textPages[3].id);
        await waitForPageToBeShown(page, textPages[3].id);
        await waitForEditablePage(page);

        await expect(await textBoxOfOtherPage(page, typedOn.id)).toHaveText(
            typed,
        );
        await expect
            .poll(() => savedTextOfPage(page, typedOn.id))
            .toContain(typed);
        const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
        expect(html).not.toContain("bloom-book-grid");
        expect((html.match(/class="bloom-page /g) ?? []).length).toBe(
            (await getPages(page)).length,
        );
    });

    for (const view of ["one", "all"] as const) {
        test(`the page can be typed in after coming back from the Collections tab and from the Publish tab (${view === "one" ? "One Page" : "All Pages"})`, async ({
            page,
        }) => {
            test.setTimeout(300000);
            await goToPage(page, textPages[0].id);
            await choosePageView(page, view);

            for (const tab of ["collection", "publish"] as const) {
                // THE ACTION UNDER TEST: leave the Edit tab and come back.
                await switchTab(page, tab);
                await switchTab(page, "edit");
                await waitForEditablePage(page);
                await waitForEditTabSettled(page);

                if (view === "all") await expectGridToMatchBook(page);
                const typed = `Typed after the ${tab} tab, ${view}`;
                await typeInGroup(page, kGroup, "en", typed);
                expect(await textOfShownPage(page)).toBe(typed);
            }
        });
    }
});
