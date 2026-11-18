// A new book is named "Book-xxxxxxxx" until it has a title. The first save after the person types
// a title renames the book's folder and file to match it, and that save happens when they click
// another page. Bloom serves the page being edited as a simulated file inside the book's folder
// ("<folder>/page-memsim-<id>.htm"), so the page it goes to next must be asked for under the NEW
// folder. Asked for under the old one, Bloom no longer has it and reports "Page expired" (BL-17055).
//
// Under --e2e Bloom does not show that report (NonFatalProblem writes it to stderr and returns), so
// the problem dialog watcher never sees it. What the test can see is the request: Bloom answers a
// simulated page it does not have with a 404, so this watches for any such answer.

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

/** Start recording every simulated page Bloom fails to serve. Returns the list it fills. */
function recordFailedSimulatedPages(page: Page): string[] {
    const failed: string[] = [];
    page.on("response", (response) => {
        if (response.status() >= 400 && response.url().includes("-memsim-"))
            failed.push(`${response.status()} ${decodeURI(response.url())}`);
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

/** Check the book now lives in a folder named for its title, and Bloom served every page. */
async function expectRenamedWithoutExpiredPages(
    oldFolder: string,
    title: string,
    failedSimulatedPages: string[],
) {
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
    expect(
        failedSimulatedPages,
        "Bloom failed to serve a simulated page (the 'Page expired' problem)",
    ).toEqual([]);
}

test("going to another page after typing a new book's title shows that page [BL-17055]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const title = "How Airplanes Fly";
    const { oldFolder, contentPageId } = await makeUntitledBook(page);

    const failedSimulatedPages = recordFailedSimulatedPages(page);
    await typeInGroup(page, ".bookTitle", "en", title);
    // Going to another page is what saves the title, and so what renames the folder.
    await selectPage(page, contentPageId);

    await expectRenamedWithoutExpiredPages(
        oldFolder,
        title,
        failedSimulatedPages,
    );
});

test("going to another page after typing a title and setting the cover picture's copyright shows that page [BL-17055]", async ({
    page,
}) => {
    // The reporter's steps: title, a picture on the cover, the picture's Copyright and License
    // dialog, then another page.
    test.setTimeout(300000);
    const title = "How Kites Fly";
    const { oldFolder, contentPageId } = await makeUntitledBook(page);

    const failedSimulatedPages = recordFailedSimulatedPages(page);
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
    // Open the picture's dialog the way its toolbar button does; the dialog is the setup here, not
    // the thing under test.
    const imageSrc = await editablePageFrame(page)
        .locator(".bloom-canvas .bloom-backgroundImage img")
        .first()
        .getAttribute("src");
    expect(imageSrc, "test setup: the cover shows the bird").toContain("bird");
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
    await selectPage(page, contentPageId);

    await expectRenamedWithoutExpiredPages(
        oldFolder,
        title,
        failedSimulatedPages,
    );
});
