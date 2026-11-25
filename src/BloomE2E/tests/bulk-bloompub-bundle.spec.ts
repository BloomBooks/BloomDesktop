// Publish: BloomPUB's "Make All BloomPUBs from Collection", with "Compress into a single
// .bloombundle file" ticked, makes one .bloombundle named after the collection's bookshelf, holding
// a .bloompub for every book in the collection and the bookshelf's .bloomshelf file.
//
// Automates the manual test "Bulk Publish Bloombundle" (Test Case ID 194).
//
// The case starts from a collection of a tester's (Koen's "Yumi Read Level 3"); the test makes its
// own collection of two books instead, under the Test enterprise subscription that the command
// needs, with the first of that subscription's bookshelves set in its settings.
//
// What stays manual, and why, is in the pull request: following the "What's this?" links, which
// open the system browser, out of the test's reach; and opening the bundle in Bloom Reader, which
// has no end-to-end suite. The test checks the bundle in the form Bloom Reader receives it: a tar
// of .bloompub zips and a .bloomshelf.

import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    listZipEntries,
    makeAllBloomPubs,
    openMakeAllBloomPubsDialog,
    readBloomBundle,
    readBookshelfFile,
    setMakeAllBloomPubsOption,
    type IBundleEntry,
} from "../helpers/bulkBloomPub";
import { selectBook } from "../helpers/collection";
import {
    kEnterpriseSubscriptionCode,
    kTestBookshelves,
} from "../helpers/collectionSettings";
import { switchTab } from "../helpers/workspace";

const BOOKSHELF = kTestBookshelves[0];

test.use({
    collectionSpec: {
        name: "bulk-bloompub-bundle",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
        bookshelf: BOOKSHELF,
    },
});

const BOOK_TITLES = ["Bulk BloomPUB Book One", "Bulk BloomPUB Book Two"];

/**
 * Make a Basic Book with a title and one page of text, leave it saved, and return its folder.
 * Setup, not the behavior under test.
 */
async function makeBookWithText(page: Page, title: string): Promise<string> {
    await switchTab(page, "collection");
    await makeBookFromTemplate(page, "Basic Book"); // lands on the cover in the Edit tab
    await typeInGroup(page, ".bookTitle", "en", title);
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, ".bloom-translationGroup", "en", `${title}.`);
    // Leave the content page (back to the cover) so Bloom saves what was typed.
    await goToPage(page, (await getPages(page))[0].id);
    return findBookFolder(page, title);
}

/** The file of this name in the bundle; fails the test, naming what the bundle has, if none. */
function bundleEntry(entries: IBundleEntry[], name: string): IBundleEntry {
    const entry = entries.find((candidate) => candidate.name === name);
    if (!entry) {
        throw new Error(
            `The bundle has no ${name}. It has: ${entries.map((e) => e.name).join(", ")}`,
        );
    }
    return entry;
}

test("Make All BloomPUBs makes a .bloombundle named after the bookshelf [Test Case ID 194]", async ({
    page,
    step,
}) => {
    const bookFolders = await step("Make two books", async () => {
        const folders: string[] = [];
        for (const title of BOOK_TITLES) {
            folders.push(await makeBookWithText(page, title));
        }
        return folders;
    });
    if (!bookFolders) throw new Error("The books were not made.");

    await step("Open Make All BloomPUBs from Collection", async () => {
        await switchTab(page, "collection");
        await selectBook(page, bookFolders[0]);
        await openMakeAllBloomPubsDialog(page);
    });

    await step("Ask for a .bloomshelf file and a .bloombundle", async () => {
        await setMakeAllBloomPubsOption(page, "bookshelfFile", true);
        await setMakeAllBloomPubsOption(page, "bloomBundle", true);
    });

    const result = await step("Make the BloomPUBs", () =>
        makeAllBloomPubs(page),
    );
    if (!result) throw new Error("Make All BloomPUBs did not run.");

    const bundleName = `${BOOKSHELF}.bloombundle`;
    const bookshelfFileName = `${BOOKSHELF}.bloomshelf`;
    const bloomPubNames = bookFolders.map(
        (folder) => `${Path.basename(folder)}.bloompub`,
    );

    await step("Check the bundle is named after the bookshelf", () => {
        expect(
            result.files,
            `Bloom made these files in ${result.folder}. The progress box said:\n${result.log}`,
        ).toContain(bundleName);
    });

    const entries = readBloomBundle(Path.join(result.folder, bundleName));

    await step("Check the bundle holds every book and the bookshelf", () => {
        expect(entries.map((entry) => entry.name).sort()).toEqual(
            [...bloomPubNames, bookshelfFileName].sort(),
        );
    });

    await step("Check each BloomPUB in the bundle is a whole book", () => {
        for (const name of bloomPubNames) {
            const files = listZipEntries(bundleEntry(entries, name).content);
            expect(files, `${name} has no meta.json`).toContain("meta.json");
            expect(
                files.filter((file) => file.endsWith(".htm")),
                `${name} should hold the book's one HTML file`,
            ).toHaveLength(1);
        }
    });

    await step("Check the bookshelf file names the bookshelf", () => {
        const shelf = readBookshelfFile(
            bundleEntry(entries, bookshelfFileName).content,
        );
        expect(shelf.id).toBe(BOOKSHELF);
    });
});
