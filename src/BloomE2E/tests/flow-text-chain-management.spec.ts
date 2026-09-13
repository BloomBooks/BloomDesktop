// What happens to a group of linked text boxes when the page itself is operated on: duplicated,
// or given a different layout. A chain is a claim about particular boxes, so a copy of a page
// must not join it, and a change of layout must not lose it.
//
// Text that moves onto another page leaves the pages after it to be refitted, and that work does
// not start by itself: it waits for the author to ask for it or to turn a page. So a step here
// that goes on to another page of the run asks for the waiting refit first (runPendingReflow),
// rather than turning the page and racing the refit the turn would start.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import { goToPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertRunIsIntact,
    clearBox,
    clickContinueText,
    enableFlowTextFeature,
    getBookChains,
    getBoxTexts,
    getChainId,
    hasOverflowMarker,
    isContinueButtonShown,
    kFlowTextCollection,
    kFlowTextFeatures,
    kParagraphsForSeveralPages,
    kTextForSeveralPages,
    kTextTooLongForOneBox,
    makeLinkedTwoBoxPage,
    pasteText,
    runPendingReflow,
    typeParagraphAtEnd,
    typeParagraphs,
    unlinkTextBox,
} from "../helpers/flowText";
import { deletePage, duplicatePageWithButton } from "../helpers/pageList";
import {
    chooseSectionType,
    sections,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";

// The same collection object as every other flow-text spec, which is what lets all of them
// run on one Bloom rather than one each. kFlowTextCollection says how that works and why a
// test here cannot be disturbed by the file before it.
test.use({
    collectionSpec: kFlowTextCollection,
    experimentalFeatures: kFlowTextFeatures,
});

// Flow text needs a paid subscription as well as the feature token above. Without it Bloom does
// not offer the feature at all and every test here fails at once; see kFlowTextCollection.
test.beforeAll(async ({ bloomApp }) => {
    await enableFlowTextFeature(bloomApp.page);
});

test.describe.configure({ mode: "serial" });

// The page with the two linked boxes.
let linkedPageId: string;

test.describe("linked text boxes and page operations", () => {
    test("builds a book with a page of two linked boxes", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        linkedPageId = await makeLinkedTwoBoxPage(page);

        expect(await getChainId(page, 0)).toBeTruthy();
        expect(await getChainId(page, 1)).toBe(await getChainId(page, 0));
    });

    test("a duplicate of the page is not linked and has no overflow mark [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, linkedPageId);

        // THE ACTION UNDER TEST: duplicate the page whose boxes are linked.
        const copy = await duplicatePageWithButton(page);
        await goToPage(page, copy.id);

        // The copy keeps the text, because it is a copy of the page. What it must not keep is
        // the claim that its boxes are the same boxes the original's text flows through: the
        // two pages would then share one chain and push text into each other.
        expect(await getChainId(page, 0)).toBeUndefined();
        expect(await getChainId(page, 1)).toBeUndefined();
        expect(await hasOverflowMarker(page, 1)).toBe(false);

        // And the original is untouched.
        await goToPage(page, linkedPageId);
        expect(await getChainId(page, 0)).toBeTruthy();
    });

    test("Unlink separates the boxes and leaves the text where it is [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, linkedPageId);
        // The second box gets more than fits in it, so that after the unlink it has text of
        // its own to say something about.
        await typeParagraphAtEnd(page, 1, kTextTooLongForOneBox);
        const before = await getBoxTexts(page);

        // THE ACTION UNDER TEST: Unlink text box, from the second box's right-click menu.
        await unlinkTextBox(page, 1);

        expect(await getChainId(page, 0)).toBeUndefined();
        expect(await getChainId(page, 1)).toBeUndefined();
        expect(await getBoxTexts(page)).toEqual(before);

        // The first box holds only what fits in it, so it does not say that its text runs
        // out. The second box holds more than fits, and now has nowhere to send the rest, so
        // it says where its own text runs out. It holds text, so it offers nothing.
        expect(await hasOverflowMarker(page, 0)).toBe(false);
        expect(await hasOverflowMarker(page, 1)).toBe(true);
        expect(await isContinueButtonShown(page, 1)).toBe(false);
    });

    test("Change Layout on a linked page keeps the chain [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // A page of its own, because the tests before this one have unlinked the boxes of the
        // page they share.
        await makeLinkedTwoBoxPage(page);
        const chainBefore = await getChainId(page, 0);
        expect(chainBefore).toBeTruthy();
        const textBefore = await getBoxTexts(page);

        // THE ACTION UNDER TEST: change the layout of the page, by adding one more text
        // section to it. Bloom rebuilds the page from the layout, and the boxes have to come
        // back still claiming the same chain.
        await setChangeLayoutMode(page, true);
        const sectionCount = await sections(page).count();
        await splitSection(page, "bottom", sectionCount - 1);
        await chooseSectionType(page, "text", sectionCount);
        await setChangeLayoutMode(page, false);

        expect(await getChainId(page, 0)).toBe(chainBefore);
        expect(await getChainId(page, 1)).toBe(chainBefore);
        // The new box is the page's own; it did not join the run.
        expect(await getChainId(page, 2)).toBeUndefined();
        // The boxes are a different size now, so the text divides between them differently.
        // What must not change is the run itself.
        assertRunIsIntact(await getBoxTexts(page), textBefore.join(" "));
    });
});

// The same questions for a chain that crosses pages, where the boxes the operation acts on are
// not in the browser at all.
test.describe("linked pages and page operations", () => {
    // The three pages of one run of text.
    let runPageIds: string[] = [];

    test("builds three pages holding one run of text", async ({ page }) => {
        test.setTimeout(300000);
        const first = await addJustTextPage(page);
        await typeParagraphs(page, 0, kParagraphsForSeveralPages);
        const second = await addJustTextPage(page);
        await clickContinueText(page, 0);
        const third = await addJustTextPage(page);
        await clickContinueText(page, 0);
        runPageIds = [first, second, third];

        const chain = (await getBookChains(page)).find((candidate) =>
            candidate.groups.some((group) => group.pageId === first),
        );
        expect(chain?.groups.map((group) => group.pageId)).toEqual(runPageIds);
    });

    test("Unlink on a continued page leaves its text there and breaks the run [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await goToPage(page, runPageIds[2]);
        const before = (await getBoxTexts(page))[0];
        expect(before.length).toBeGreaterThan(0);

        // THE ACTION UNDER TEST: Unlink text box on the last page of the run.
        await unlinkTextBox(page, 0);

        // The text that had flowed onto this page is now this page's own text.
        expect((await getBoxTexts(page))[0]).toBe(before);
        expect(await getChainId(page, 0)).toBeUndefined();
        // Nothing offers to continue it: the box holds text, so it has nothing to offer, and
        // the pages before it hold no more than fits.
        expect(await isContinueButtonShown(page, 0)).toBe(false);

        // The two pages before it are still one run, and C# says so for the whole book.
        const chain = (await getBookChains(page)).find((candidate) =>
            candidate.groups.some((group) => group.pageId === runPageIds[0]),
        );
        expect(chain?.groups.map((group) => group.pageId)).toEqual([
            runPageIds[0],
            runPageIds[1],
        ]);
    });

    test("deleting a page in the middle of a run leaves the rest flowing [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // Put the last page back into the run, so there is a middle page to delete. Emptying the
        // last box took that page out of the chain, so the run is put back in: the run is three
        // pages' worth of text, so it reaches the third page again and the offer there returns.
        //
        // All of it goes in at once rather than paragraph by paragraph. This box is already part
        // of a chain, so text typed into it moves off it as soon as it overflows, and Bloom
        // follows the moved words to the next page, caret and all: the paragraphs after that one
        // would be typed into whatever box the caret had landed in.
        await goToPage(page, runPageIds[2]);
        await clearBox(page, 0);
        await goToPage(page, runPageIds[0]);
        await pasteText(page, 0, kTextForSeveralPages);
        // Putting the run back in pushed text onto the page after this one, which leaves the rest
        // of the run to be refitted. Run it here rather than letting the page turn below start
        // it, or the offer this test then takes would be read while the refit was still moving.
        await runPendingReflow(page);
        await goToPage(page, runPageIds[2]);
        await clickContinueText(page, 0);
        expect(
            (await getBookChains(page)).find((candidate) =>
                candidate.groups.some(
                    (group) => group.pageId === runPageIds[0],
                ),
            )?.groups,
        ).toHaveLength(3);

        // THE ACTION UNDER TEST: delete the middle page of the run.
        await deletePage(page, runPageIds[1]);

        // The deleted page's text has moved into the box beside it, so the rest of the run has to
        // be divided again, and that refit waits for the author. The reading below is of the
        // chain, which the refit does not change, but the clearBox further down is on a page the
        // refit would be rewriting.
        await runPendingReflow(page);

        const chain = (await getBookChains(page)).find((candidate) =>
            candidate.groups.some((group) => group.pageId === runPageIds[0]),
        );
        expect(chain?.groups.map((group) => group.pageId)).toEqual([
            runPageIds[0],
            runPageIds[2],
        ]);

        // And the text still flows between the two pages that are left: emptying the first
        // brings the last page's text back into it.
        await goToPage(page, runPageIds[0]);
        await clearBox(page, 0);
        expect((await getBoxTexts(page))[0].length).toBeGreaterThan(0);
    });
});
