// A "|" in a text box tells the Talking Book tool where to split a sentence into segments. The bar
// shows only while that tool is open: away from it, Bloom keeps each bar as a hidden marker, so it
// is not part of what a reader sees on the page or in a publication.
//
// Automates the manual test "Segment Marker Shows Only While the Tool Is Open" (Test Case ID 504).
//
// The text is typed before the toolbox opens rather than with the tool open, as the case has it:
// typing with the Talking Book tool open can stall the page's updates to Bloom (BL-17186). What the
// case checks starts after the typing, when the page is saved and reloaded with the tool open, so
// the bar still makes the trip through Bloom's hidden marker that the case is about.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    getPreviewTextBox,
    openPublishDestination,
    showBloomPubPreview,
    showPreviewPage,
} from "../helpers/publish";
import { openToolboxWithTalkingBook } from "../helpers/talkingBook";
import { hideToolbox } from "../helpers/toolbox";

const TEXT_WITH_MARKER = "Two |words";
const TEXT_SEEN_WITHOUT_TOOL = "Two words";

test.use({
    collectionSpec: { name: "talking-book-segment-marker", languages: ["en"] },
});

test("the segment marker shows only while the Talking Book tool is open [Test Case ID 504]", async ({
    page,
}) => {
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text", 1);
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, ".bloom-translationGroup", "en", TEXT_WITH_MARKER);
    await openToolboxWithTalkingBook(page);

    // Leaving the page saves it, with the bar turned into Bloom's hidden marker; coming back with
    // the tool open must turn the marker back into a bar the person can see.
    await reloadPageBeingEdited(page);
    const box = await clickInGroup(page, ".bloom-translationGroup", "en");
    await expect(
        box,
        'With the Talking Book tool open, the reloaded page should show the "|" the person typed.',
    ).toHaveText(TEXT_WITH_MARKER, { timeout: 60000, useInnerText: true });

    await hideToolbox(page);
    await expect(
        box,
        'With the Talking Book tool closed, the "|" should no longer show in the text box.',
    ).toHaveText(TEXT_SEEN_WITHOUT_TOOL, {
        timeout: 30000,
        useInnerText: true,
    });

    await openPublishDestination(page, "BloomPUB");
    const player = await showBloomPubPreview(page);
    const previewPage = await showPreviewPage(player, contentPage.id);
    const previewBox = getPreviewTextBox(previewPage, "Two");
    await expect(
        previewBox,
        'The BloomPUB preview should not show the "|" segment marker.',
    ).toHaveText(TEXT_SEEN_WITHOUT_TOOL, {
        timeout: 30000,
        useInnerText: true,
    });
});
