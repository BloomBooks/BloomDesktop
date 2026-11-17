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
import {
    commitInputMethodText,
    composeWithInputMethod,
    insertTextWithNoKeyPress,
    pressProcessKey,
} from "../helpers/keys";
import { clickPageThumbnail } from "../helpers/pageList";
import { choosePdfMode, getPdfPreviewPageCount } from "../helpers/pdfPrint";
import {
    choosePageView,
    clickTextOfPageInGrid,
    expectGridToMatchBook,
    getFocusedGroupIndex,
    isPageChangeUnderway,
    startRecordingEachFrame,
    stopRecordingEachFrame,
    textBoxOfOtherPage,
    waitForOtherPagesOnScreenToBeDrawn,
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

    // Text from an input method or voice typing, which may come with no key press at all. The page
    // gets the text before its editors are ready, then more once they are. Both pieces must stay
    // together in the box.
    for (const input of [
        {
            name: "voice typing or Keyman, which press no key",
            target: 1,
            composes: false,
            pressesKey: false,
            composingWhenReady: false,
        },
        {
            name: "an input method that presses no key",
            target: 2,
            composes: true,
            pressesKey: false,
            composingWhenReady: false,
        },
        {
            name: "an input method that sends Windows' key press for each change",
            target: 3,
            composes: true,
            pressesKey: true,
            composingWhenReady: false,
        },
        {
            name: "an input method still composing a word when the editors are ready",
            target: 5,
            composes: true,
            pressesKey: true,
            composingWhenReady: true,
        },
    ]) {
        test(`with All Pages, text from ${input.name}, given before and after the clicked page is ready, stays together in the box`, async ({
            page,
        }) => {
            // Two bugs, each found by this test:
            // - The click that opened the page is replayed once its editors are ready, unless the
            //   person pressed a key, clicked, or left the page in the meantime
            //   (replayOpeningClick() in bookGridView.ts). Text that comes with no key press does
            //   not count, so the replay moves the cursor from the end of that text back to where
            //   the page was clicked, and the next words go in there.
            // - When the editors finish starting, keepTypingWhenCkEditorLoads() and CKEditor
            //   rewrite the text box. A word still being composed then stays in the box as typed
            //   letters, and the word the person chooses goes in beside them instead of replacing
            //   them.
            test.fail(
                !input.pressesKey,
                "Bug: text that comes with no key press does not stop the replayed click from moving the cursor (replayOpeningClick() in bookGridView.ts).",
            );
            test.fail(
                input.composingWhenReady,
                "Bug: a word being composed when the editors finish starting is left in the box, and the chosen word goes in beside it (keepTypingWhenCkEditorLoads.ts).",
            );
            const leaving = textPages[input.target - 1];
            const target = textPages[input.target];
            await goToPage(page, leaving.id);
            await expectGridToMatchBook(page);
            await waitForEditTabSettled(page);
            const textBefore = (
                await (await textBoxOfOtherPage(page, target.id)).innerText()
            ).trim();
            const textOfLeftPage = await textOfShownPage(page);
            const hold = await holdCkEditorStartup(page);

            // THE ACTION UNDER TEST: click the other page's text and give text at once, before the
            // page's editors are ready; then, once they are, give more.
            await clickTextOfPageInGrid(page, target.id);
            await waitForPageToBeShown(page, target.id);
            await hold.requested;
            await waitForFocusInGroup(page, 0);
            expect(
                await getStatusOfFocusedEditor(page),
                "test setup: the text box's editor should still be starting when the text arrives",
            ).not.toBe("ready");
            const first = "FIRST WORDS";
            const second = " AND MORE";
            const editorsAreReady = async () => {
                await hold.release();
                await waitForClickedPageToSettle(page);
                await waitForEditTabSettled(page);
            };
            // A word as an input method gives it: the letters typed so far, shown but not yet
            // committed, then the word chosen, which replaces them.
            const composeWord = async (
                word: string,
                then?: () => Promise<void>,
            ) => {
                if (input.pressesKey) await pressProcessKey(page);
                await composeWithInputMethod(page, word.slice(0, 3));
                if (then) await then();
                if (input.pressesKey) await pressProcessKey(page);
                await commitInputMethodText(page, word);
            };
            if (!input.composes) {
                await insertTextWithNoKeyPress(page, first);
                await editorsAreReady();
                await insertTextWithNoKeyPress(page, second);
            } else if (input.composingWhenReady) {
                await composeWord(first, editorsAreReady);
                await composeWord(second);
            } else {
                await composeWord(first);
                await editorsAreReady();
                await composeWord(second);
            }

            const text = await textOfShownPage(page);
            expect(
                text.replace(first + second, ""),
                `The two pieces of text should be side by side in the box, which held "${textBefore}" before, but it now reads "${text}".`,
            ).toBe(textBefore);
            await expect(await textBoxOfOtherPage(page, leaving.id)).toHaveText(
                textOfLeftPage,
            );
            await waitForBloomToHaveTyping(page, first + second);
            await goToPage(page, leaving.id);
            await expect
                .poll(() => savedTextOfPage(page, target.id))
                .toContain(first + second);
        });
    }

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

    for (const view of ["one", "all"] as const) {
        test(`clicking quickly through ten pages in the page list shows a drawn page on every frame, and the page list never moves (${view === "one" ? "One Page" : "All Pages"})`, async ({
            page,
        }) => {
            const pages = (await getPages(page)).slice(0, 11);
            expect(
                pages.length,
                "test setup: the book should have eleven pages",
            ).toBe(11);
            await goToPage(page, pages[0].id);
            await choosePageView(page, view);
            await waitForEditTabSettled(page);
            if (view === "all") await waitForOtherPagesOnScreenToBeDrawn(page);
            await startRecordingEachFrame(page);

            // THE ACTION UNDER TEST: ten clicks, none waiting for the page before it to show.
            for (const next of pages.slice(1))
                await clickPageThumbnail(page, next.id);

            const last = pages[pages.length - 1];
            await waitForPageToBeShown(page, last.id);
            await waitForEditablePage(page);
            if (view === "all") await expectGridToMatchBook(page);
            const frames = await stopRecordingEachFrame(page);
            expect(frames.length).toBeGreaterThan(10);
            const bookPages = new Set(pages.map((p) => p.id));
            frames.forEach((f, i) => {
                const where = `Animation frame ${i} of ${frames.length} (${f.frame})`;
                // With All Pages, the page being left stays on screen until the next one is drawn
                // with the pages around it. (With One Page, the frame is blank while a page loads.)
                // The page being left scrolls the clicked page into view, and the pages that
                // brings on screen are drawn a few frames later, so only the new page's frame is
                // checked for pages not drawn.
                if (view === "all") {
                    expect(
                        f.pageId && bookPages.has(f.pageId),
                        `${where} showed no page of the book.`,
                    ).toBe(true);
                    expect(
                        f.showsOtherPages,
                        `${where} did not show the other pages.`,
                    ).toBe(true);
                    if (f.frame === "page")
                        expect(
                            f.undrawnPagesOnScreen,
                            `${where} showed page ${f.pageId} with pages around it not drawn.`,
                        ).toEqual([]);
                }
                expect(f.pageListLeft, `${where}: the page list moved.`).toBe(
                    frames[0].pageListLeft,
                );
                expect(
                    f.scrolledAroundPageList,
                    `${where}: something around the page list scrolled.`,
                ).toBe("");
            });
            expect(frames[frames.length - 1].pageId).toBe(last.id);
            await waitForEditTabSettled(page);
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

    test("with All Pages, after typing on a page and clicking another page in the grid, the PDF has the book's own pages and nothing more", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const typedOn = textPages[3];
        const clicked = textPages[4];
        await goToPage(page, typedOn.id);
        await choosePageView(page, "all");
        await typeInGroup(page, kGroup, "en", "Typed before making the PDF");

        // THE ACTION UNDER TEST: a real click on another page, then the PDF.
        await clickTextOfPageInGrid(page, clicked.id);
        await waitForPageToBeShown(page, clicked.id);
        await waitForEditablePage(page);
        const pageCount = (await getPages(page)).length;
        await switchTab(page, "publish");
        await choosePdfMode(page, "Simple");

        expect(
            await getPdfPreviewPageCount(page),
            "The PDF should have one page for each page of the book.",
        ).toBe(pageCount);
        await switchTab(page, "edit");
        await waitForEditablePage(page);
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
