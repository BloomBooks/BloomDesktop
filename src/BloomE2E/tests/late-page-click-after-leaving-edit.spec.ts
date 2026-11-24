// A page click that Bloom handles only after the user has gone to another tab (BL-17089).
//
// The user clicks a page in the Edit tab's page list and then, at once, the Publish tab, and Bloom
// handles the tab change first. Bloom used to act on the click anyway, starting to open the page
// in the hidden Edit tab. Publish's page layout update then loads every page of the book
// off-screen, and the off-screen copy of that page made the hidden Edit tab believe it was editing
// it, so leaving Publish failed with "Cannot empty page while editing". Now the click is kept for
// when the user comes back to Edit, and only the Edit tab's own load of a page counts.
//
// The late click is the one step that does not go through the UI; see
// deliverLatePageThumbnailClick for why it cannot.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    getShownPageId,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    makeBookNeedPageLayoutUpdate,
    waitForPageLayoutUpdateRecorded,
} from "../helpers/pageLayoutUpdate";
import { deliverLatePageThumbnailClick } from "../helpers/pageList";
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
    // The new page is the one being edited, so the late click goes to another page: the cover.
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    const [cover] = await getPages(page);
    // Publish runs the page layout update, which is what loads every page off-screen, only for a
    // book that needs it.
    await makeBookNeedPageLayoutUpdate(page, bookFolder);
    expect(
        await getShownPageId(page),
        "test setup: the Edit tab should not already be showing the page the late click is for",
    ).not.toBe(cover.id);

    await switchTab(page, "publish");
    expect(
        await deliverLatePageThumbnailClick(page, cover.id),
        "Bloom started opening a page in the Edit tab while that tab was hidden.",
    ).toBe(false);

    await openPublishDestinationScreen(page, "BloomPUB");
    await waitForPageLayoutUpdateRecorded(bookFolder);

    // Leaving Publish is what failed. A problem dialog fails the test through the fixture.
    await switchTab(page, "collection");

    await switchTab(page, "edit");
    // The page that was clicked is the one shown on coming back, and the Edit tab still works.
    await waitForEditablePage(page, cover.id);
    await selectPage(page, contentPage.id);
});
