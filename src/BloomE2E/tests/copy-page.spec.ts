// Copying a page must carry everything on it: the custom style, the image, the Talking Book
// recording, the video, and the custom origami layout — both when the page is pasted back into
// its own book and when it is pasted into another book in the same Bloom.
//
// This automates the manual case "Copy Page Preserves Everything". Copying between two separate
// Bloom instances is a third case in the manual test; it is known not to work in 6.5 and is out
// of scope here, so this test is Partial coverage. See AUTOMATION-DEBT.md.
//
// The page under test cannot be built through the UI: adding an image, a recording, or a video
// needs a native file dialog or a microphone, which an e2e test must never open. So the page
// comes ready-made from the `page-copy` collection in bloom-testing-inputs. The copy and the
// paste themselves, which are what this test measures, go through the real page menu.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    bookFileExists,
    readBook,
    waitForBookWithPageCount,
    type IPageContents,
} from "../helpers/bookHtml";
import { selectBook } from "../helpers/collection";
import {
    getPageIds,
    markEditablePage,
    runPageMenuCommand,
    selectPage,
    waitForEditablePageReload,
    waitForPageCount,
} from "../helpers/pageThumbnails";
import { switchTab } from "../helpers/workspace";

test.use({ collectionName: "page-copy" });

// The layout the fixture page was built from. Bloom's "Custom" page template, whose id is fixed
// in src/content/templates/template books/standard-page-mixins.pug.
const CUSTOM_LAYOUT_TEMPLATE_ID = "5dcd48df-e9ab-4a07-afd4-6a24d0398386";

// The user-defined style the fixture's page carries. Bloom keeps the rule for it in the book's
// own userModifiedStyles block, so pasting into another book has to carry the rule across too.
const CUSTOM_STYLE_CLASS = "PageCopyMarker-style";

/**
 * Fail unless the page really has all five things the manual test says a copy must preserve.
 * Run against the ORIGINAL before anything is copied, so a later pass cannot be vacuous, and
 * against each pasted page afterwards.
 */
function expectPageHasEverything(
    page: IPageContents,
    bookFolder: string,
    what: string,
): void {
    expect(page.lineage, `${what}: the page's layout template`).toContain(
        CUSTOM_LAYOUT_TEMPLATE_ID,
    );
    expect(page.styleClasses, `${what}: the custom style class`).toContain(
        CUSTOM_STYLE_CLASS,
    );

    expect(page.imageSources, `${what}: images on the page`).toHaveLength(1);
    expect(
        bookFileExists(bookFolder, page.imageSources[0]),
        `${what}: the image file ${page.imageSources[0]} is missing from ${bookFolder}`,
    ).toBe(true);

    expect(
        page.audioSentenceIds,
        `${what}: recorded sentences on the page`,
    ).toHaveLength(1);
    expect(
        bookFileExists(bookFolder, `audio/${page.audioSentenceIds[0]}.mp3`),
        `${what}: the recording audio/${page.audioSentenceIds[0]}.mp3 is missing from ${bookFolder}`,
    ).toBe(true);

    expect(page.videoSources, `${what}: videos on the page`).toHaveLength(1);
    expect(
        bookFileExists(bookFolder, page.videoSources[0]),
        `${what}: the video file ${page.videoSources[0]} is missing from ${bookFolder}`,
    ).toBe(true);

    // Three slots, so two splits: the image over the rest, then the video over the text.
    expect(page.layout, `${what}: the page's origami layout`).toHaveLength(2);
}

test("copying a page preserves everything, within and between books [Test Case ID 348]", async ({
    page,
    bloomApp,
}) => {
    const sourceBook = Path.join(bloomApp.collectionDir, "Copy Source");
    const destinationBook = Path.join(
        bloomApp.collectionDir,
        "Copy Destination",
    );

    // ---- The page we are about to copy really does have all five ingredients ----------------
    await selectBook(page, sourceBook);
    await switchTab(page, "edit");

    const sourceBefore = await readBook(page, sourceBook);
    expect(
        sourceBefore.pages,
        "The source book should start with two numbered pages.",
    ).toHaveLength(2);
    const original = sourceBefore.pages[0];
    expectPageHasEverything(original, sourceBook, "the original page");
    expect(
        sourceBefore.userModifiedStyles,
        "The source book should define the custom style before anything is copied.",
    ).toContain(`.${CUSTOM_STYLE_CLASS}`);

    // ---- Copy and paste it, through the page menu, inside its own book -----------------------
    const pageIdsBefore = await getPageIds(page);
    expect(
        pageIdsBefore,
        "The page under test should be a thumbnail in the pane.",
    ).toContain(original.id);

    await selectPage(page, original.id);
    await markEditablePage(page);
    await runPageMenuCommand(page, original.id, "Copy Page");
    // Copy Page saves the book first, which makes Bloom reload the page. Pasting before that
    // finishes does nothing at all, so wait for the page to come back.
    await waitForEditablePageReload(page, original.id);
    await runPageMenuCommand(page, original.id, "Paste Page");
    await waitForPageCount(page, pageIdsBefore.length + 1);

    // Leaving the Edit tab makes Bloom save the book, which is what puts the pasted page in the
    // file we are about to read. It is also the way back to the collection for the second half.
    await switchTab(page, "collection");
    const sourceAfter = await waitForBookWithPageCount(page, sourceBook, 3);

    const originalIndex = sourceAfter.pages.findIndex(
        (p) => p.id === original.id,
    );
    expect(
        originalIndex,
        "The original page should still be in the source book after the paste.",
    ).toBeGreaterThanOrEqual(0);
    const copyInSameBook = sourceAfter.pages[originalIndex + 1];
    expect(
        copyInSameBook,
        "Bloom should have inserted the pasted page after the page that was copied.",
    ).toBeDefined();
    expect(
        copyInSameBook.id,
        "The pasted page should be a new page, not the one that was copied.",
    ).not.toBe(original.id);

    expectPageHasEverything(
        copyInSameBook,
        sourceBook,
        "the page pasted into the same book",
    );
    expect(
        copyInSameBook.styleClasses,
        "The pasted page should carry exactly the styles the original had.",
    ).toEqual(original.styleClasses);
    expect(
        copyInSameBook.layout,
        "The pasted page should have the original's custom layout.",
    ).toEqual(original.layout);
    expect(
        copyInSameBook.imageSources,
        "The pasted page should show the same image file.",
    ).toEqual(original.imageSources);

    // ---- Paste the same page into a different book in the same Bloom -------------------------
    await selectBook(page, destinationBook);
    await switchTab(page, "edit");

    const destinationBefore = await readBook(page, destinationBook);
    expect(
        destinationBefore.pages,
        "The destination book should start with one numbered page.",
    ).toHaveLength(1);
    expect(
        destinationBefore.userModifiedStyles,
        "The destination book should not know the custom style before the paste.",
    ).not.toContain(`.${CUSTOM_STYLE_CLASS}`);
    const destinationPageBefore = destinationBefore.pages[0];

    // The thumbnail pane counts front and back matter too, so compare against what it shows now
    // rather than against the book's numbered-page count.
    const destinationThumbnailsBefore = (await getPageIds(page)).length;
    await selectPage(page, destinationPageBefore.id);
    await runPageMenuCommand(page, destinationPageBefore.id, "Paste Page");
    await waitForPageCount(page, destinationThumbnailsBefore + 1);

    await switchTab(page, "collection");
    const destinationAfter = await waitForBookWithPageCount(
        page,
        destinationBook,
        2,
    );

    const copyInOtherBook = destinationAfter.pages.find(
        (p) => p.id !== destinationPageBefore.id,
    )!;
    expect(
        copyInOtherBook,
        "The destination book should have gained the pasted page.",
    ).toBeDefined();

    // Every file the page refers to has to have come across into this book's own folder.
    expectPageHasEverything(
        copyInOtherBook,
        destinationBook,
        "the page pasted into the other book",
    );
    expect(
        copyInOtherBook.styleClasses,
        "The page pasted into the other book should carry the original's styles.",
    ).toEqual(original.styleClasses);
    expect(
        copyInOtherBook.layout,
        "The page pasted into the other book should keep the original's custom layout.",
    ).toEqual(original.layout);

    // The class alone would render as plain text; the rule that defines it has to travel too.
    expect(
        destinationAfter.userModifiedStyles,
        "Bloom should have copied the custom style's rule into the destination book.",
    ).toContain(`.${CUSTOM_STYLE_CLASS}`);
});
