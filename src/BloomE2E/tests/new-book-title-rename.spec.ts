// A new book is named "Book-xxxxxxxx" until it has a title. The first full save after the person
// types a title renames the book's folder and file to match it. That save can come from clicking
// another page, or from anything else that saves the book, such as closing a picture's Copyright
// and License dialog. Bloom serves the page being edited as a simulated file inside the book's
// folder ("<folder>/page-memsim-<id>.htm"), and the page's pictures and scripts are links relative
// to it. So once the folder is renamed, anything still asked for under the old folder fails: the
// next page itself ("Page expired"), a picture on the page (shown as missing), or a script the
// book's template puts on its pages (BL-17055).
//
// Under --e2e Bloom does not show those reports (NonFatalProblem writes them to stderr and returns),
// so the problem dialog watcher never sees them. What the test can see is the requests: this
// watches for any that failed and named the old folder.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    editablePageFrame,
    getContentPages,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { apiPost } from "../helpers/api";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import { pageListFrame, selectPage } from "../helpers/pageThumbnails";
import type { Page } from "@playwright/test";
import { failedRequestsUnder } from "../helpers/failedRequests";

test.use({
    collectionSpec: { name: "new-book-title-rename", languages: ["en"] },
});

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

const failureMessage =
    "Bloom failed to serve something under the book's old folder: the next page ('Page expired'), " +
    "a picture, or a script";

/**
 * Make a book from the Big Book template (the one the reporter used) and return its folder and
 * the id of a content page to go to.
 */
async function makeUntitledBook(
    page: Page,
): Promise<{ oldFolder: string; contentPageId: string }> {
    const oldFolder = await makeBookFromTemplate(page, "Big Book");
    expect(
        Path.basename(oldFolder),
        "test setup: a book with no title yet has a placeholder folder name",
    ).toMatch(/^Book/);
    const contentPages = await getContentPages(page);
    expect(
        contentPages.length,
        "test setup: the Big Book template has a content page to go to",
    ).toBeGreaterThan(0);
    return { oldFolder, contentPageId: contentPages[0].id };
}

/** Wait until the book lives in a folder named for its title, and the old folder is gone. */
async function expectRenamed(oldFolder: string, title: string) {
    const newFolder = Path.join(Path.dirname(oldFolder), title);
    await expect
        .poll(() => fs.existsSync(newFolder), {
            message: `The book's folder was never renamed to ${newFolder}.`,
        })
        .toBe(true);
    expect(
        fs.existsSync(oldFolder),
        "the folder the book was renamed from should be gone",
    ).toBe(false);
}

test("going to another page after typing a new book's title asks for nothing under the old folder [BL-17055]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const title = "How Airplanes Fly";
    const { oldFolder, contentPageId } = await makeUntitledBook(page);

    const failed = await failedRequestsUnder(page, oldFolder, async () => {
        await typeInGroup(page, ".bookTitle", "en", title);
        // Going to another page is what saves the title here, and so what renames the folder.
        await selectPage(page, contentPageId);
        await expectRenamed(oldFolder, title);
    });

    expect(failed, failureMessage).toEqual([]);
});

test("closing the cover picture's Copyright and License dialog after typing a title keeps the picture showing [BL-17055]", async ({
    page,
}) => {
    // The reporter's steps: title, a picture on the cover, the picture's Copyright and License
    // dialog, then another page. Closing the dialog saves the book, so the rename happens there,
    // while the cover is still showing.
    test.setTimeout(300000);
    const title = "How Kites Fly";
    const { oldFolder, contentPageId } = await makeUntitledBook(page);

    await typeInGroup(page, ".bookTitle", "en", title);
    await chooseImageFile(page, fixtureImage("bird.png"));
    expect(
        fs.existsSync(oldFolder),
        "test setup: choosing a picture does not rename the folder",
    ).toBe(true);

    const failed = await failedRequestsUnder(page, oldFolder, async () => {
        // Open the picture's dialog the way its toolbar button does; opening it is setup, not
        // the thing under test.
        const imageSrc = await editablePageFrame(page)
            .locator(".bloom-canvas .bloom-backgroundImage img")
            .first()
            .getAttribute("src");
        await page.evaluate(
            (src) =>
                (
                    window as unknown as {
                        workspaceBundle: {
                            showCopyrightAndLicenseDialog: (
                                url: string,
                            ) => void;
                        };
                    }
                ).workspaceBundle.showCopyrightAndLicenseDialog(src),
            imageSrc!,
        );
        await setCopyrightHolder(page, "Kite Society");

        await expectRenamed(oldFolder, title);
        await expect
            .poll(
                async () => {
                    const placement = await getImagePlacement(page);
                    return placement.loaded
                        ? placement.fileName
                        : "(not loaded)";
                },
                {
                    timeout: 30000,
                    message:
                        "The cover picture never showed after the dialog closed.",
                },
            )
            .toBe("bird.png");

        await selectPage(page, contentPageId);
    });

    expect(failed, failureMessage).toEqual([]);
});

test("a save that stays on the page after typing a title leaves the page working from the new folder [BL-17055]", async ({
    page,
}) => {
    // Opening the AI image editor saves the book without going to another page, so when that save
    // renames the folder, the page on screen was loaded from the old one. Bloom must show it again
    // from the new folder: otherwise a picture chosen on it afterwards is looked for in the old
    // folder and shows as missing.
    test.setTimeout(300000);
    const title = "How Boats Float";
    const { oldFolder } = await makeUntitledBook(page);
    await chooseImageFile(page, fixtureImage("bird.png"));
    await typeInGroup(page, ".bookTitle", "en", title);
    expect(
        fs.existsSync(oldFolder),
        "test setup: typing a title does not rename the folder by itself",
    ).toBe(true);

    const failed = await failedRequestsUnder(page, oldFolder, async () => {
        // The endpoint the picture's "AI Image Editor" command posts. Opening the editor is
        // setup; the save it does first is what renames the folder.
        await apiPost(
            page,
            "aiImageEditor/saveThenLaunch",
            JSON.stringify({ slotIndex: 0 }),
            "application/json",
        );
        await expectRenamed(oldFolder, title);
        await expect
            .poll(
                () =>
                    page.evaluate(
                        () =>
                            typeof (
                                window as unknown as {
                                    __bloomAiImageEditorCleanup?: () => void;
                                }
                            ).__bloomAiImageEditorCleanup === "function",
                    ),
                {
                    timeout: 30000,
                    message: "The AI image editor never opened.",
                },
            )
            .toBe(true);
        await page.evaluate(() =>
            (
                window as unknown as { __bloomAiImageEditorCleanup: () => void }
            ).__bloomAiImageEditorCleanup(),
        );
        await waitForEditTabSettled(page);

        // The page list from the new folder must still draw its thumbnails: it is told apart from
        // one left over from the old folder only by its own address. The cover's thumbnail shows
        // the book as saved, so the bird.
        const coverId = await editablePageFrame(page)
            .locator(".bloom-page")
            .first()
            .getAttribute("id");
        const birdInCoverThumbnail = pageListFrame(page)
            .locator(
                `#pageGridWrapper .gridItem[id="${coverId}"] img[src*="bird.png"]`,
            )
            .first();
        await expect
            .poll(
                () =>
                    birdInCoverThumbnail
                        .evaluate((img) => {
                            const image = img as HTMLImageElement;
                            return image.complete && image.naturalWidth > 0;
                        })
                        .catch(() => false),
                {
                    timeout: 30000,
                    message:
                        "The page list's cover thumbnail never showed the cover picture.",
                },
            )
            .toBe(true);

        await chooseImageFile(page, fixtureImage("house-landscape.png"));
    });

    expect(failed, failureMessage).toEqual([]);
});
