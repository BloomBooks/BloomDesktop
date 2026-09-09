// What happens to a group of linked text boxes when the page itself is operated on: duplicated,
// or given a different layout. A chain is a claim about particular boxes, so a copy of a page
// must not join it, and a change of layout must not lose it.
//
// The Test Case IDs in the titles are marked TBD: this feature has no rows in the Notion test
// inventory yet, and the ids are allocated there when it lands, not by this file.
//
// The tests are serial because each one starts from the book the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import { goToPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    getBoxTexts,
    getChainId,
    hasOverflowMarker,
    isContinueButtonShown,
    makeLinkedTwoBoxPage,
    unlinkTextBox,
} from "../helpers/flowText";
import { duplicatePageWithButton } from "../helpers/pageList";

test.use({
    collectionSpec: { name: "flow-text-chains", languages: ["en"] },
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

    // Change Layout keeps a chain because HtmlDom.MigrateChildren copies data-flow-chain along
    // with the rest of a translation group's attributes -- which is a C# change that belongs to
    // the cross-page phase, and is not in yet. The helper to drive it exists
    // (helpers/origami.ts: setChangeLayoutMode, chooseSectionType), so this test is a few lines
    // once that lands. See AUTOMATION-DEBT.md, "Change Layout drops a text box's flow chain".
    test.fixme(
        "Change Layout on a linked page keeps the chain [Test Case ID TBD]",
        async () => {
            // Blocked: see the comment above.
        },
    );
});
