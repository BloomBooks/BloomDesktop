// Deleting a page that holds part of a run of text carried through linked text boxes.
//
// The text of a chained box belongs to a run that carries on in other boxes, so it cannot go with
// the page: Bloom moves it into the box beside it in the chain and refits the chain from there
// (FlowTextChains.MoveChainedTextOffPage, then FlowTextWalk). What this test is about is that
// nothing of the run is lost or repeated when the page in the middle of it goes.
//
// The run is read out of the book through C# (getBookChains) rather than off a page, and the Edit
// tab is parked outside the chain first: Bloom writes the page being edited into the book only
// when the book moves off it, so the book's copy of that page lags behind the browser's until
// then.

import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    addJustTextPage,
    assertRunIsIntact,
    clickContinueText,
    getBookChains,
    kTextForSeveralPages,
    pasteText,
    waitForReflowIdle,
} from "../helpers/flowText";
import { deletePage } from "../helpers/pageList";

test.use({
    collectionSpec: { name: "flow-text-delete-page", languages: ["en"] },
});

test.describe("deleting a page that holds part of a flow", () => {
    test("the text of the deleted page stays in the run [Test Case ID TBD]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        const firstPageId = await addJustTextPage(page);
        await pasteText(page, 0, kTextForSeveralPages);
        const secondPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);
        const thirdPageId = await addJustTextPage(page);
        await clickContinueText(page, 0);

        const chainPages = [firstPageId, secondPageId, thirdPageId];

        /**
         * Leave the chain, so that the page being edited is written into the book and what the
         * book says about the chain is all of a piece.
         */
        const parkOffTheChain = async (): Promise<void> => {
            const parking = (await getPages(page)).find(
                (candidate) => !chainPages.includes(candidate.id),
            );
            if (!parking) {
                throw new Error(
                    "Every page of this book belongs to the chain, so there is nowhere to park.",
                );
            }
            await goToPage(page, parking.id);
            await waitForReflowIdle(page);
        };

        // Sanity check the starting point: one run of text spread over the three pages, whole.
        await parkOffTheChain();
        const before = await getBookChains(page);
        expect(
            before,
            "The setup must leave exactly one chain for this test to delete from.",
        ).toHaveLength(1);
        expect(before[0].groups.map((group) => group.pageId)).toEqual(
            chainPages,
        );
        const textsBefore = before[0].groups.map(
            (group) => group.textByLang["en"],
        );
        assertRunIsIntact(textsBefore, kTextForSeveralPages);
        expect(
            textsBefore[1].length,
            "The middle page must hold text of its own, or deleting it would prove nothing.",
        ).toBeGreaterThan(0);

        // THE ACTION UNDER TEST: delete the middle page of the chain.
        await deletePage(page, secondPageId);
        await waitForReflowIdle(page);

        await parkOffTheChain();
        const after = await getBookChains(page);
        expect(after).toHaveLength(1);
        expect(
            after[0].groups.map((group) => group.pageId),
            "The chain now runs through the two pages that are left.",
        ).toEqual([firstPageId, thirdPageId]);

        const textsAfter = after[0].groups.map(
            (group) => group.textByLang["en"],
        );
        // The whole point: the text that was on the deleted page is still in the run, in its
        // place, with nothing lost at either of the joins the move made.
        assertRunIsIntact(textsAfter, kTextForSeveralPages);
    });
});
