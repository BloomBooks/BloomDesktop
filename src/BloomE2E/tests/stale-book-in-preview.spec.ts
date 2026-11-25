// A book selected in one collection must not stay in the Collections tab's preview after the user
// changes to a collection that does not have it (Test Case ID 770, from BL-14313). Bloom keeps one
// book selection for the whole application, so it outlives the collection; the preview must
// ignore it until a book of the new collection is selected.
//
// The Other Collection dialog offers the collections Bloom opened recently, so the test first
// visits a second collection, and makes its book there. The collection it then chooses in the
// dialog is the one it started in, which has no books.

import type { Page } from "@playwright/test";
import { test } from "../fixtures/bloomTest";
import { makeBookFromTemplate, typeInGroup } from "../helpers/bookMaking";
import {
    chooseOtherCollection,
    expectBookInPreview,
    expectNoBookInPreview,
    makeAnotherCollection,
    openOtherCollection,
} from "../helpers/collection";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "stale-preview-first", languages: ["en"] },
});

/**
 * Make a book with `title` on its cover in the collection Bloom has open, and go back to the
 * Collections tab, where the new book is the selected one.
 */
async function makeTitledBookAndShowCollection(
    page: Page,
    title: string,
): Promise<void> {
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", title);
    await switchTab(page, "collection");
}

test("a book selected in one collection is not previewed in the next [Test Case ID 770]", async ({
    bloomApp,
    step,
}) => {
    // Two books made, and a whole workspace closed and built again twice.
    test.setTimeout(400000);
    const firstCollection = bloomApp.collectionDir;
    const leftBehindTitle = "Left Behind";
    const newTitle = "Newly Made";

    await step("Select a book in the current collection", async () => {
        const secondCollection = makeAnotherCollection(bloomApp, {
            name: "stale-preview-second",
            languages: ["en"],
        });
        await openOtherCollection(bloomApp, secondCollection);
        await makeTitledBookAndShowCollection(bloomApp.page, leftBehindTitle);
        // Sanity check: the preview does show this book, so its absence below means something.
        await expectBookInPreview(bloomApp.page, leftBehindTitle);
    });

    await step("Choose a collection that does not have that book", async () => {
        await chooseOtherCollection(bloomApp, firstCollection);
    });

    await step("See that the preview is empty", async () => {
        await expectNoBookInPreview(bloomApp.page);
    });

    await step(
        "Select a book in this collection and see it previewed",
        async () => {
            await makeTitledBookAndShowCollection(bloomApp.page, newTitle);
            await expectBookInPreview(bloomApp.page, newTitle);
        },
    );
});
