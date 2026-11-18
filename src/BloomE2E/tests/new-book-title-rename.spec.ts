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
} from "../helpers/bookMaking";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { chooseImageFile } from "../helpers/images";
import { selectPage } from "../helpers/pageThumbnails";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "new-book-title-rename", languages: ["en"] },
});

/** The cover's picture, in the page being edited. */
const COVER_PICTURE = ".bloom-canvas .bloom-backgroundImage img";

/**
 * Start recording every request that failed and named `folder` (the book's folder before the
 * rename). Returns the list it fills.
 */
function recordFailedRequestsUnder(page: Page, folder: string): string[] {
    const folderName = Path.basename(folder);
    const failed: string[] = [];
    page.on("response", (response) => {
        const url = decodeURI(response.url());
        if (response.status() >= 400 && url.includes(folderName))
            failed.push(`${response.status()} ${url}`);
    });
    return failed;
}

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

/** Wait until the cover's picture is `fileName` and has loaded. */
async function expectCoverPictureShowing(page: Page, fileName: string) {
    await expect
        .poll(
            () =>
                editablePageFrame(page)
                    .locator(COVER_PICTURE)
                    .first()
                    .evaluate((img) => {
                        const image = img as HTMLImageElement;
                        return image.complete && image.naturalWidth > 0
                            ? decodeURIComponent(
                                  (image.getAttribute("src") ?? "")
                                      .split("?")[0]
                                      .split("/")
                                      .pop() ?? "",
                              )
                            : "(not loaded)";
                    })
                    .catch(() => "(no cover picture)"),
            {
                timeout: 30000,
                message: `The cover never showed ${fileName}.`,
            },
        )
        .toBe(fileName);
}

const failureMessage =
    "Bloom failed to serve something under the book's old folder: the next page ('Page expired'), " +
    "a picture, or a script";

test("going to another page after typing a new book's title asks for nothing under the old folder [BL-17055]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const title = "How Airplanes Fly";
    const { oldFolder, contentPageId } = await makeUntitledBook(page);

    const failed = recordFailedRequestsUnder(page, oldFolder);
    await typeInGroup(page, ".bookTitle", "en", title);
    // Going to another page is what saves the title here, and so what renames the folder.
    await selectPage(page, contentPageId);

    await expectRenamed(oldFolder, title);
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
    await chooseImageFile(
        page,
        Path.resolve(
            Path.dirname(fileURLToPath(import.meta.url)),
            "..",
            "fixtures",
            "images",
            "bird.png",
        ),
    );
    await expectCoverPictureShowing(page, "bird.png");
    expect(
        fs.existsSync(oldFolder),
        "test setup: choosing a picture does not rename the folder",
    ).toBe(true);

    const failed = recordFailedRequestsUnder(page, oldFolder);
    // Open the picture's dialog the way its toolbar button does; opening it is setup, not the
    // thing under test.
    const imageSrc = await editablePageFrame(page)
        .locator(COVER_PICTURE)
        .first()
        .getAttribute("src");
    await page.evaluate(
        (src) =>
            (
                window as unknown as {
                    workspaceBundle: {
                        showCopyrightAndLicenseDialog: (url: string) => void;
                    };
                }
            ).workspaceBundle.showCopyrightAndLicenseDialog(src),
        imageSrc!,
    );
    await setCopyrightHolder(page, "Kite Society");

    await expectRenamed(oldFolder, title);
    await expectCoverPictureShowing(page, "bird.png");
    expect(failed, failureMessage).toEqual([]);

    await selectPage(page, contentPageId);
    expect(failed, failureMessage).toEqual([]);
});
