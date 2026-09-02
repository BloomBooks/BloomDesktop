// Duplicating a page: every way the Edit tab offers to do it, and the promise that a copy is its
// own page, so that changing the copy leaves the original alone. Automates the manual test
// "Duplicate Page" (Test Case ID 349).
//
// The manual test also re-records audio and trims a video on the copy. Neither can be automated
// today: recording needs a microphone, and a video arrives only through a native file picker. Nor
// can "Duplicate Page Many Times" be driven, because its dialog is a WinForms surface CDP cannot
// reach. See AUTOMATION-DEBT.md for all three.
//
// The tests are serial because each one starts from the book the one before it left behind.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    chooseImageFile,
    cropImage,
    getImagePlacement,
} from "../helpers/images";
import {
    duplicatePageWithButton,
    duplicatePageWithContextMenu,
    movePageToSlotOf,
} from "../helpers/pageList";

test.use({
    collectionSpec: { name: "duplicate-page", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The picture the first content page gets. Small, and shipped with the suite, so the test needs
// nothing from outside this folder. It is a copy of src/BloomTests/ImageProcessing/images/bird.png.
const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// The page with the picture, and the copy made of it. Set by the first two tests.
let original: IBookPage;
let duplicate: IBookPage;

/** The ids of the book's pages, in order, for comparing orders. */
const idsOf = (pages: IBookPage[]) => pages.map((p) => p.id);

/** Assert that, in `pages`, the copy sits in the slot right after the page it was made from. */
const expectCopyRightAfter = (
    pages: IBookPage[],
    sourceId: string,
    copyId: string,
) => {
    const ids = idsOf(pages);
    expect(ids.indexOf(sourceId)).toBeGreaterThanOrEqual(0);
    expect(ids[ids.indexOf(sourceId) + 1]).toBe(copyId);
};

test.describe("duplicating a page", () => {
    test("builds a book with one picture page", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        [original] = await getContentPages(page);
        await goToPage(page, original.id);
        await typeInGroup(page, ".bloom-translationGroup", "en", "Original");
        await chooseImageFile(page, IMAGE_FILE);

        // Sanity check the page the rest of the file rests on: it shows the picture, uncropped.
        const placement = await getImagePlacement(page);
        expect(placement.fileName).toBe("bird.png");
        expect(placement.cropped).toBe(false);
    });

    test("the Duplicate button makes a copy right after the page [Test Case ID 349]", async ({
        page,
    }) => {
        await goToPage(page, original.id);
        const before = await getPages(page);
        duplicate = await duplicatePageWithButton(page);

        const after = await getPages(page);
        expect(after.length).toBe(before.length + 1);
        expectCopyRightAfter(after, original.id, duplicate.id);
        // The copy has the same picture, uncropped, like the original.
        await goToPage(page, duplicate.id);
        const placement = await getImagePlacement(page);
        expect(placement.fileName).toBe("bird.png");
        expect(placement.cropped).toBe(false);
    });

    test("cropping the picture on the copy leaves the original uncropped [Test Case ID 349]", async ({
        page,
    }) => {
        await goToPage(page, duplicate.id);
        // THE ACTION UNDER TEST: a real drag on the copy's picture.
        await cropImage(page, "e", 80);
        expect((await getImagePlacement(page)).cropped).toBe(true);

        // Leaving the page is what saves it; coming back shows what was saved.
        await goToPage(page, original.id);
        expect((await getImagePlacement(page)).cropped).toBe(false);

        await goToPage(page, duplicate.id);
        expect((await getImagePlacement(page)).cropped).toBe(true);
    });

    test("the right-click menu duplicates a page [Test Case ID 349]", async ({
        page,
    }) => {
        await goToPage(page, original.id);
        const before = await getPages(page);
        const copy = await duplicatePageWithContextMenu(page, original.id);
        const after = await getPages(page);
        expect(after.length).toBe(before.length + 1);
        expectCopyRightAfter(after, original.id, copy.id);
    });

    test("a page can be duplicated right after it is added [Test Case ID 349]", async ({
        page,
    }) => {
        const before = await getPages(page);
        await addPage(page, "Just Text");
        const added = (await getPages(page)).find(
            (p) => !before.some((b) => b.id === p.id),
        )!;
        await goToPage(page, added.id);
        const copy = await duplicatePageWithButton(page);
        const after = await getPages(page);
        expect(after.length).toBe(before.length + 2);
        expectCopyRightAfter(after, added.id, copy.id);
    });

    test("duplicating works after reordering, and reordering works after duplicating [Test Case ID 349]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        const contentPages = await getContentPages(page);
        expect(contentPages.length).toBeGreaterThanOrEqual(3);
        const [first, second, third] = contentPages;

        // Reorder: move the first content page onto the third's slot.
        await movePageToSlotOf(page, first.id, third.id);
        const reordered = idsOf(await getContentPages(page));
        expect(reordered.indexOf(first.id)).toBe(2);
        expect(reordered.indexOf(second.id)).toBe(0);

        // Then duplicate the page that moved: the copy follows it in its new place.
        await goToPage(page, first.id);
        const copy = await duplicatePageWithButton(page);
        const afterDuplicatePages = await getContentPages(page);
        expectCopyRightAfter(afterDuplicatePages, first.id, copy.id);
        const afterDuplicate = idsOf(afterDuplicatePages);

        // Then reorder again: the copy moves to the front, and every other page keeps its order.
        await movePageToSlotOf(page, copy.id, second.id);
        const afterMove = idsOf(await getContentPages(page));
        expect(afterMove[0]).toBe(copy.id);
        expect(afterMove.filter((id) => id !== copy.id)).toEqual(
            afterDuplicate.filter((id) => id !== copy.id),
        );
    });
});
