// A page click that Bloom handles only after the user has gone to another tab (BL-17089).
//
// The user clicks a page in the Edit tab's page list and then, at once, the Publish tab, and Bloom
// handles the tab change first. Bloom used to act on the click anyway, starting to open the page
// in the hidden Edit tab. Publish's page layout update then loads every page of the book
// off-screen, and the off-screen copy of that page made the hidden Edit tab believe it was editing
// it, so leaving Publish failed with "Cannot empty page while editing". Now the click is kept for
// when the user comes back to Edit, and only the Edit tab's own load of a page counts.
//
// The order of the click and the tab change cannot be produced by clicking (see
// deliverLatePageThumbnailClick), so the late click is the one step that goes straight to Bloom's
// API. Everything the test measures, the tab changes in particular, goes through the real UI.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { deliverLatePageThumbnailClick } from "../helpers/pageList";
import {
    getPageSize,
    getSavedPageLayoutUpdateLevel,
    setPageSize,
} from "../helpers/pageSize";
import { selectPage, waitForEditablePage } from "../helpers/pageThumbnails";
import { openPublishDestinationScreen } from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "late-page-click", languages: ["en"] },
});

test("a page click handled after leaving Edit is shown on return, and leaving Publish still works [Test Case ID 861]", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text", 3);
    const [first, second, third] = await getContentPages(page);
    await goToPage(page, first.id);

    // Publish brings the book's pages up to date, loading each one off-screen, only when they need
    // it, and a change of page size is what makes them need it.
    const otherSize =
        (await getPageSize(page)) === "A5Portrait"
            ? "LetterPortrait"
            : "A5Portrait";
    await setPageSize(page, otherSize);
    expect(
        getSavedPageLayoutUpdateLevel(bookFolder),
        "Setup failed: after a change of page size the book should need the page layout update.",
    ).toBe(0);

    await switchTab(page, "publish");
    expect(
        await deliverLatePageThumbnailClick(page, second.id),
        "Bloom started opening a page in the Edit tab while that tab was hidden.",
    ).toBe(false);

    // Publish shows the BloomPUB screen only once the page layout update has finished, so this
    // waits until every page has been loaded off-screen.
    await openPublishDestinationScreen(page, "BloomPUB");
    expect(
        getSavedPageLayoutUpdateLevel(bookFolder),
        "Publish did not run the page layout update, so the off-screen page loads this test is about never happened.",
    ).toBeGreaterThan(0);

    // Leaving Publish is what failed. A problem dialog fails the test through the fixture.
    await switchTab(page, "collection");

    await switchTab(page, "edit");
    // The page that was clicked is the one shown on coming back, and the Edit tab works.
    await waitForEditablePage(page, second.id);
    await selectPage(page, third.id);
});
