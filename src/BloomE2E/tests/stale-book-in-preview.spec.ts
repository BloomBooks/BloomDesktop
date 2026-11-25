// A book selected in one collection must not stay in the Collections tab's preview after the user
// changes to a collection that does not have it (Test Case ID 770, from BL-14313). Bloom keeps one
// book selection for the whole application, so it outlives the collection; the preview must
// ignore it until a book of the new collection is selected.
//
// The change of collection is made with the call that a click on a collection's card makes. The
// dialog with the cards, behind the Other Collection button, is in a WebView2 that a test cannot
// reach (AUTOMATION-DEBT.md: "WinForms surfaces are invisible to CDP").

import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    expectBookInPreview,
    expectNoBookInPreview,
    getSelectedBookId,
    makeAnotherCollection,
    openOtherCollection,
} from "../helpers/collection";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "stale-preview-first", languages: ["en"] },
});

test("a book selected in one collection is not previewed in the next [Test Case ID 770]", async ({
    bloomApp,
    step,
}) => {
    // Two books made, and a whole workspace closed and built again in between.
    test.setTimeout(300000);

    const firstBookId = await step(
        "Select a book in the current collection",
        async () => {
            await makeBookFromTemplate(bloomApp.page, "Basic Book");
            await switchTab(bloomApp.page, "collection");
            const id = await getSelectedBookId(bloomApp.page);
            if (!id)
                throw new Error(
                    "Test setup: Bloom has no book selected after making one.",
                );
            // Sanity check: the preview does show this book, so its absence below means something.
            await expectBookInPreview(bloomApp.page, id);
            return id;
        },
    );

    await step("Choose a collection that does not have that book", async () => {
        const otherCollection = makeAnotherCollection(bloomApp, {
            name: "stale-preview-second",
            languages: ["en"],
        });
        await openOtherCollection(bloomApp, otherCollection);
    });

    await step("See that the preview is empty", async () => {
        await expectNoBookInPreview(bloomApp.page);
    });

    await step(
        "Select a book in this collection and see it previewed",
        async () => {
            await makeBookFromTemplate(bloomApp.page, "Basic Book");
            await switchTab(bloomApp.page, "collection");
            const secondBookId = await getSelectedBookId(bloomApp.page);
            expect(
                secondBookId,
                "the book made in the second collection should be a different book",
            ).not.toBe(firstBookId);
            if (!secondBookId)
                throw new Error(
                    "Bloom has no book selected after making one in the second collection.",
                );
            await expectBookInPreview(bloomApp.page, secondBookId);
        },
    );
});
