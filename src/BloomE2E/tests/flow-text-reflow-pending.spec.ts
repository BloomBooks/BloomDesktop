// When the pages a run of text carries on into are refitted, and who decides.
//
// Text never moves across pages on its own. The browser settles the page it is editing and hands
// what no longer fits to the box on the next page, but the pages after THAT are Bloom's to
// measure, and that work holds a progress dialog over the window for seconds. So it does not
// start by itself: Bloom records that the chain is out of date (flowText/pendingWalks), and the
// author says when it happens -- now, with a button, or whenever they change pages, if a
// checkbox says so.
//
// Both of those live in a bubble that a chained box has only while a refit of its chain is
// waiting: there is no bubble when there is nothing to do, and the bubble goes when the refit has
// run. So these tests read whether the bubble is there, with flowText/pendingWalks as the second
// source of truth.
//
// Three "Just Text" pages holding one run of text, so that there is a page beyond the one the
// browser hands text to: that third page is the one only a refit can put right, and reading it
// out of the book (through the chain C# reports, with no page turn) is how these tests tell a
// refit that has happened from one that has not. A page turn is one of the two things that starts
// a refit, so a test that read the pages by visiting them could not tell the difference.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import * as fs from "node:fs";
import * as Path from "node:path";

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertRunIsIntact,
    clickContinueText,
    clickReflowNow,
    getBookChains,
    getBoxParagraphTexts,
    getReflowBubbleTexts,
    getReflowOnPageChange,
    getRunTexts,
    isProgressDialogOpen,
    isReflowPendingShown,
    isWalkPending,
    kFlowTextCollection,
    kTextForSeveralPages,
    runPendingReflow,
    setReflowOnPageChange,
    setReflowOnPageChangeViaBubble,
    typeNewParagraphAfter,
    typeParagraphs,
    waitForReflowIdle,
} from "../helpers/flowText";

// The same collection object as every other flow-text spec, which is what lets all of them
// run on one Bloom rather than one each. kFlowTextCollection says how that works and why a
// test here cannot be disturbed by the file before it.
test.use({ collectionSpec: kFlowTextCollection });

test.describe.configure({ mode: "serial" });

/** The words the bubble says, from BloomMediumPriority.xlf by way of flowConstants.ts. */
const kPendingText = "Reflow pending";
const kOnPageChangeText = "Reflow when you change pages";
const kReflowNowText = "Reflow now";

/**
 * The run of text, as paragraphs. The first page's box has to hold more than one paragraph, so
 * that a test can add a paragraph in the MIDDLE of it: a paragraph added at the very end of a
 * full page moves off it as it is typed, and Bloom follows the words onto the next page, which
 * would leave these tests looking at a page they did not change.
 */
const kParagraphs = splitIntoParagraphs(kTextForSeveralPages, 8);

/** Cut a body of text into `count` paragraphs of about equal length, never inside a word. */
function splitIntoParagraphs(text: string, count: number): string[] {
    const words = text.split(" ").filter((word) => word.length > 0);
    const perParagraph = Math.ceil(words.length / count);
    const paragraphs: string[] = [];
    for (let at = 0; at < words.length; at += perParagraph) {
        paragraphs.push(words.slice(at, at + perParagraph).join(" "));
    }
    return paragraphs;
}

/**
 * A paragraph of about 200 characters whose every word is its own, so the words a person typed
 * can be told from the words that were already in the run. `mark` keeps the paragraphs of two
 * tests apart.
 */
function typedParagraph(mark: string): string {
    return Array.from(
        { length: 33 },
        (_unused, i) => mark + String(i + 1).padStart(4, "0"),
    ).join(" ");
}

// The book, and the three pages the run of text carries through.
let bookFolder: string;
let firstPageId: string;
let secondPageId: string;
let thirdPageId: string;

// What the last page of the run held before the change that left a refit waiting, so that the
// test which runs the refit can say the refit reached it.
let lastPageTextBeforeTheChange: string;

/**
 * The text of one page of the run as the BOOK holds it, read through the chain C# reports. No page
 * is turned and no page is opened, so the reading neither starts a waiting refit nor settles the
 * page it is about: that is the whole reason these tests read the book rather than the pages.
 *
 * The page being edited is the one exception, because Bloom writes that page only when the book
 * moves off it; no test here reads the page it is editing this way.
 */
async function textOfPageInBook(page: Page, pageId: string): Promise<string> {
    const chains = await getBookChains(page);
    expect(
        chains,
        "These tests are about one run of text, so the book must hold exactly one chain.",
    ).toHaveLength(1);
    const group = chains[0].groups.find(
        (candidate) => candidate.pageId === pageId,
    );
    if (!group) {
        throw new Error(
            `Page ${pageId} is not in the book's chain. The chain runs through ` +
                `${chains[0].groups.map((each) => each.pageId).join(", ")}.`,
        );
    }
    return group.textByLang["en"];
}

/**
 * What the book's own file says about running the refit on a page change, or undefined when the
 * file says nothing about it. Bloom writes the setting only when it is false: a book nobody has
 * changed it for has no entry, and reading such a book gets the default, which is true.
 */
function reflowOnPageChangeInBookFile(): boolean | undefined {
    const path = Path.join(bookFolder, "book.userPrefs");
    if (!fs.existsSync(path)) {
        return undefined;
    }

    const prefs = JSON.parse(fs.readFileSync(path, "utf8")) as Record<
        string,
        unknown
    >;
    const value = prefs["flowTextReflowOnPageChange"];
    return value === undefined ? undefined : !!value;
}

test.describe("a refit of the later pages that waits for the author", () => {
    test("builds three pages holding one run of text", async ({ page }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        firstPageId = await addJustTextPage(page);
        await typeParagraphs(page, 0, kParagraphs);
        secondPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);
        thirdPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);

        const chains = await getBookChains(page);
        expect(chains).toHaveLength(1);
        expect(chains[0].groups.map((group) => group.pageId)).toEqual([
            firstPageId,
            secondPageId,
            thirdPageId,
        ]);
        assertRunIsIntact(await getRunTexts(page), kParagraphs.join(" "));
        expect(
            await getReflowOnPageChange(page),
            "A book nobody has changed the setting for runs the refit on a page change.",
        ).toBe(true);
        // Reading the run visited every page of it, and opening a page can move text between it
        // and the page after it, so run whatever that left waiting: the test below is about what
        // ITS own change leaves waiting, and starts from a book with nothing outstanding.
        await runPendingReflow(page);
    });

    test("typing leaves the refit of the pages after this one waiting [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, firstPageId);

        // Sanity check the start state: nothing is waiting, nothing says otherwise, and the
        // first page holds more than one paragraph, so the paragraph typed below goes in the
        // middle of its text rather than at the end. Coming to this page is itself a page
        // change, which runs anything the setting says to run, so the wait is for that.
        await expect
            .poll(() => isWalkPending(page), {
                timeout: 30000,
                message:
                    "The book still has a refit waiting before this test has changed anything.",
            })
            .toBe(false);
        // With nothing waiting there is no bubble to offer the author anything to do.
        expect(await isReflowPendingShown(page)).toBe(false);
        expect(
            (await getBoxParagraphTexts(page, 0)).length,
            "The first page has to hold more than one paragraph; see kParagraphs.",
        ).toBeGreaterThanOrEqual(2);
        lastPageTextBeforeTheChange = await textOfPageInBook(page, thirdPageId);
        expect(
            lastPageTextBeforeTheChange.length,
            "The last page of the run must hold text, or a refit reaching it proves nothing.",
        ).toBeGreaterThan(0);

        // THE ACTION UNDER TEST: add a paragraph after the first page's first paragraph. The
        // text it pushes down no longer fits the page, so the browser hands what does not fit to
        // the box on the next page -- and the pages after THAT one are now out of date.
        await typeNewParagraphAfter(page, 0, 0, typedParagraph("t"));

        // The author is still on the page they typed on: the words they typed did not move.
        expect(await getShownPageId(page)).toBe(firstPageId);
        // Bloom is holding the refit rather than running it, and the page says so.
        expect(
            await isWalkPending(page),
            "Text pushed onto another page has to leave a refit of the rest waiting.",
        ).toBe(true);
        await expect
            .poll(() => isReflowPendingShown(page), {
                timeout: 15000,
                message:
                    "The page shows nothing to say a refit is waiting, so the author has no " +
                    "way to know or to ask for it.",
            })
            .toBe(true);
        expect(await getReflowBubbleTexts(page)).toEqual({
            pending: kPendingText,
            reflowOnPageChange: kOnPageChangeText,
            reflowNow: kReflowNowText,
        });

        // THE ASSERTION UNDER TEST: nothing has been done to the pages beyond the next one. The
        // last page of the run holds exactly what it held, and no progress dialog has been up.
        expect(
            await textOfPageInBook(page, thirdPageId),
            "The last page of the run changed, so the refit ran without the author asking.",
        ).toBe(lastPageTextBeforeTheChange);
        expect(await isProgressDialogOpen(page)).toBe(false);
    });

    test("Reflow now runs the refit that was waiting [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // Sanity check the start state: the refit the test before this one left is still waiting.
        expect(
            await isWalkPending(page),
            "This test runs the refit the test before it left waiting.",
        ).toBe(true);
        expect(await isReflowPendingShown(page)).toBe(true);

        // THE ACTION UNDER TEST: click Reflow now.
        const seen = await clickReflowNow(page);

        // The work happens behind the Edit tab's progress dialog, which is what tells the author
        // Bloom is busy and stops them editing while it is. A run of three short pages can be
        // over between two readings, so Bloom's own word that it was busy counts as well.
        expect(
            seen.sawProgressDialog || seen.sawBusy,
            "Nothing showed that Bloom was doing the refit: neither the progress dialog nor " +
                "the flow's own report of being busy was ever seen.",
        ).toBe(true);
        expect(
            await isProgressDialogOpen(page),
            "The dialog must go away by itself once the refit is done.",
        ).toBe(false);

        // Nothing is waiting any more, and the bubble goes: there is nothing left for it to
        // offer the author.
        expect(await isWalkPending(page)).toBe(false);
        await expect
            .poll(() => isReflowPendingShown(page), {
                timeout: 15000,
                message:
                    "The page still says a refit is waiting, though the refit has been done.",
            })
            .toBe(false);

        // The refit reached the page the browser never touched, and the run is still one run:
        // not a word lost or repeated at any of the joins it made.
        expect(
            await textOfPageInBook(page, thirdPageId),
            "The last page of the run holds what it held before, so the refit did not reach it.",
        ).not.toBe(lastPageTextBeforeTheChange);
        assertRunIsIntact(
            await getRunTexts(page),
            [kParagraphs[0], typedParagraph("t"), ...kParagraphs.slice(1)].join(
                " ",
            ),
        );
    });

    test("with the checkbox off, changing pages leaves the refit waiting [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, firstPageId);
        // The test before this one read the whole run, which visited every page of it, and
        // opening a page can move text between it and the page after it. Run whatever that left
        // waiting, so that what waits below is this test's own change.
        await runPendingReflow(page);
        // Sanity check the start state, and make a refit wait again: another paragraph in the
        // middle of the first page's text.
        expect(await isWalkPending(page)).toBe(false);
        await typeNewParagraphAfter(page, 0, 0, typedParagraph("u"));
        expect(
            await isWalkPending(page),
            "Text pushed onto another page has to leave a refit of the rest waiting.",
        ).toBe(true);
        const lastPageText = await textOfPageInBook(page, thirdPageId);

        // THE ACTION UNDER TEST: untick "Reflow when you change pages", then change pages.
        //
        // The page turned to is the last page of the run. The second page holds more than fits
        // it until the refit runs, and a page being edited settles its own boundary, so turning
        // to it would push that text onto the third page whether or not the refit ran. The last
        // page has no page after it to push onto.
        await setReflowOnPageChangeViaBubble(page, false);
        await goToPage(page, thirdPageId);

        // The page change did not run it: the refit is still waiting, the last page of the run
        // still holds what it held, and the page the author has turned to says so too, because
        // its own box is in the chain that is waiting.
        expect(
            await isWalkPending(page),
            "The author said not to refit on a page change, so changing pages must not.",
        ).toBe(true);
        expect(await textOfPageInBook(page, thirdPageId)).toBe(lastPageText);
        await expect
            .poll(() => isReflowPendingShown(page), { timeout: 15000 })
            .toBe(true);
        // And it is still waiting, and still said to be, on the way back.
        await goToPage(page, firstPageId);
        expect(await isWalkPending(page)).toBe(true);
        await expect
            .poll(() => isReflowPendingShown(page), { timeout: 15000 })
            .toBe(true);

        // THE SECOND ACTION UNDER TEST: tick it again, and change pages.
        //
        // The page turned to carries no box of the chain: a refit never writes the page being
        // edited, so a page of the run would leave the refit of its own box to the browser, and
        // what the third page then holds would not be the refit's doing alone.
        await setReflowOnPageChangeViaBubble(page, true);
        const chainPages = (await getBookChains(page)).flatMap((chain) =>
            chain.groups.map((group) => group.pageId),
        );
        const pageOffTheChain = (await getPages(page)).find(
            (candidate) => !chainPages.includes(candidate.id),
        );
        if (!pageOffTheChain) {
            throw new Error(
                "Every page of the book carries a box of the chain.",
            );
        }
        await goToPage(page, pageOffTheChain.id);

        // This time the page change is what runs the refit.
        await expect
            .poll(() => isWalkPending(page), {
                timeout: 60000,
                intervals: [200, 250, 500, 500],
                message:
                    "The author said to refit when they change pages, and changing pages did " +
                    "not run the refit that was waiting.",
            })
            .toBe(false);
        await waitForReflowIdle(page);
        expect(await textOfPageInBook(page, thirdPageId)).not.toBe(
            lastPageText,
        );
        // Back to a page of the run to read its bubble. The first page settled itself before
        // the refit, so opening it moves nothing and leaves nothing waiting.
        await goToPage(page, firstPageId);
        // And the bubble goes, because nothing is waiting for the author to ask for.
        await expect
            .poll(() => isReflowPendingShown(page), { timeout: 15000 })
            .toBe(false);
    });

    test("the setting is remembered for the book [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // Sanity check the start state: the setting is on, and because only the false value is
        // written, the book's file says nothing about it.
        expect(await getReflowOnPageChange(page)).toBe(true);
        expect(reflowOnPageChangeInBookFile()).toBeUndefined();

        // The checkbox is in the bubble, and the bubble is there only while a refit is waiting,
        // so make one wait: another paragraph in the middle of the first page's text.
        await goToPage(page, firstPageId);
        await typeNewParagraphAfter(page, 0, 0, typedParagraph("v"));
        await expect
            .poll(() => isReflowPendingShown(page), { timeout: 15000 })
            .toBe(true);

        // THE ACTION UNDER TEST: untick "Reflow when you change pages".
        await setReflowOnPageChangeViaBubble(page, false);

        // It is the book's own setting, so it is written into the book's own file, where it will
        // be found the next time the book is opened.
        await expect
            .poll(() => reflowOnPageChangeInBookFile(), {
                timeout: 15000,
                message:
                    "The book's userPrefs file does not say the author turned refitting on a " +
                    "page change off, so the next Bloom to open the book will not know.",
            })
            .toBe(false);

        // Put it back the way a book starts out, so the next test and the next run of this file
        // start from the default. Bloom leaves the default out of the file altogether.
        await setReflowOnPageChangeViaBubble(page, true);
        await expect
            .poll(() => reflowOnPageChangeInBookFile(), { timeout: 15000 })
            .toBeUndefined();
        expect(await getReflowOnPageChange(page)).toBe(true);

        // Leave nothing waiting behind this file.
        await runPendingReflow(page);
        await setReflowOnPageChange(page, true);
    });
});
