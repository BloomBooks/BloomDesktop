// Changing a page's layout keeps both the new layout and the text typed on the page.
//
// Leaving Change Layout mode is the one save that sends the page to Bloom at once and then has
// Bloom rebuild it (saveChangesAndRethinkPage and sendSnapshotNow in bloomEditing.ts and
// pageSnapshot.ts; BL-13502). If the page Bloom saves predates the change, the rebuilt page loses
// either the split or the typing.
//
// Covers part of "Change Layout Splits" (Test Case ID 366), which stays Planned for the rest: this
// test makes one split, and does not try every section type or delete splits. It is a new card;
// its Test Case ID goes in the title once the card exists.

import { expect, test } from "../fixtures/bloomTest";
import {
    makeBookFromTemplate,
    typeInGroup,
    waitForBloomToHaveTyping,
} from "../helpers/bookMaking";
import { readBook } from "../helpers/bookHtml";
import {
    addCustomTextPage,
    sections,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "change-layout-keeps-typing", languages: ["en"] },
});

test("splitting a section keeps the typing and saves the new layout", async ({
    page,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const custom = await addCustomTextPage(page);

    // Typing that Bloom already has when the layout changes.
    await typeInGroup(page, ".bloom-translationGroup", "en", "Typed first.");
    await waitForBloomToHaveTyping(page, "Typed first.");

    await setChangeLayoutMode(page, true);
    await splitSection(page, "bottom");
    // setChangeLayoutMode waits for Bloom to rebuild the page from what it saved.
    await setChangeLayoutMode(page, false);

    expect(
        await sections(page).count(),
        "The rebuilt page should have the two sections the split made.",
    ).toBe(2);
    await switchTab(page, "collection");
    const saved = (await readBook(page, bookFolder)).pages.find(
        (p) => p.id === custom.id,
    );
    expect(saved?.layout, "the saved page should have one split").toHaveLength(
        1,
    );
    expect(saved?.text).toContain("Typed first.");
});

test("typing straight before changing the layout is kept too", async ({
    page,
}) => {
    // No wait between typing and the layout change: the save on leaving Change Layout mode sends
    // the page to Bloom at once (sendSnapshotNow) rather than relying on the snapshot already sent.
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const custom = await addCustomTextPage(page);

    await typeInGroup(page, ".bloom-translationGroup", "en", "Typed just now.");
    await setChangeLayoutMode(page, true);
    await splitSection(page, "right");
    await setChangeLayoutMode(page, false);

    await switchTab(page, "collection");
    const saved = (await readBook(page, bookFolder)).pages.find(
        (p) => p.id === custom.id,
    );
    expect(saved?.layout).toHaveLength(1);
    expect(saved?.text).toContain("Typed just now.");
});
