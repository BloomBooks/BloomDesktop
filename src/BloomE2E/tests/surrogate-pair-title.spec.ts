// Test Case ID 667, "Surrogate Pair Title".
//
// A new book is named "Book-xxxxxxxx" until it has a title; the first save after the person gives
// it one renames the book's folder and file after the title. Bloom shortens a long title to make
// the name. Maithili written in the Tirhuta script lies outside Unicode's Basic Multilingual Plane,
// so each of its letters is a surrogate pair: two units of the string, either of which alone is not
// a character. A name cut between the two halves of a pair is one Windows refuses, and with such a
// title Bloom once reported an error on leaving the cover, showed another in the Collections tab,
// and could not start again on that collection (BL-12587).
//
// Under --e2e Bloom shows a NonFatalProblem to nobody and writes it to stderr, so the test reads
// those with bloomApp.takeNonFatalProblems(). The fixture itself fails the test when Bloom shows
// its "Bloom had a problem" dialog.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    clickInGroup,
    editBook,
    getPages,
    makeBookFromTemplate,
    pasteTextInGroup,
} from "../helpers/bookMaking";
import {
    listEditableBooks,
    waitForCollectionReady,
} from "../helpers/collection";
import { selectPage } from "../helpers/pageThumbnails";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "surrogate-pair-title", languages: ["en"] },
});

// The title the test case gives: Maithili in the Tirhuta script. Showing it needs no font.
const kTitle = "𑒮𑒩𑓂𑒫𑒹 𑒧𑒰𑒢𑒫𑒰𑓁 𑒮𑓂𑒫𑒞𑒢𑓂𑒞𑓂𑒩𑒰𑓁 𑒮𑒧𑒳𑒞𑓂𑒣𑒢𑓂𑒢𑒰𑓁 𑒫𑒩𑓂𑒞𑒢𑓂𑒞𑒹";

/** A surrogate that is not half of a pair, which is what a name cut inside a pair ends with. */
const kLoneSurrogate =
    /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/;

test("a pasted title in a script made of surrogate pairs names the book's files, and Bloom carries on [Test Case ID 667]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);

    expect(
        [...kTitle].some((letter) => letter.length === 2),
        "test setup: the title has letters that are surrogate pairs",
    ).toBe(true);
    expect(
        kLoneSurrogate.test(kTitle),
        "test setup: the title itself is well formed",
    ).toBe(false);

    let oldFolder = "";
    let coverId = "";
    let otherPageId = "";
    await step("Make a book from the Basic Book template", async () => {
        oldFolder = await makeBookFromTemplate(page, "Basic Book");
        expect(
            Path.basename(oldFolder),
            "test setup: a book with no title yet has a placeholder folder name",
        ).toMatch(/^Book/);
        const pages = await getPages(page);
        expect(
            pages.length,
            "test setup: the book has a page besides its cover to go to",
        ).toBeGreaterThan(1);
        coverId = pages[0].id;
        otherPageId = pages[1].id;
    });
    // Whatever Bloom reported while it made the book is not this test's subject.
    bloomApp.takeNonFatalProblems();

    await step("Paste the Maithili title into the cover's title", async () => {
        await pasteTextInGroup(page, ".bookTitle", "en", kTitle);
    });

    let bookFolder = "";
    await step(
        "Go to another page, so that the book's files take the title's name",
        async () => {
            await selectPage(page, otherPageId);
            await expect
                .poll(() => fs.existsSync(oldFolder), {
                    timeout: 30000,
                    message:
                        "The book's folder was never renamed from its placeholder name.",
                })
                .toBe(false);
            const books = await listEditableBooks(page);
            expect(
                books.length,
                "the collection has the one book, under its new name",
            ).toBe(1);
            bookFolder = books[0].folderPath;
        },
    );

    await step("Check the name that the book's files got", () => {
        const name = Path.basename(bookFolder);
        expect(
            kLoneSurrogate.test(name),
            `the folder's name, ${JSON.stringify(name)}, must not end inside a surrogate pair`,
        ).toBe(false);
        expect(
            name.length,
            "the folder is named for the title, not with a fallback such as 'Book'",
        ).toBeGreaterThan(10);
        expect(
            kTitle.normalize("NFC").startsWith(name),
            `the folder's name, ${JSON.stringify(name)}, is the start of the title`,
        ).toBe(true);
        expect(
            fs.existsSync(Path.join(bookFolder, name + ".htm")),
            "the book's file has the same name as its folder",
        ).toBe(true);
    });

    await step("Return to the Collections tab", async () => {
        await switchTab(page, "collection");
        await waitForCollectionReady(page);
        expect(
            (await listEditableBooks(page)).map((book) => book.folderPath),
            "the Collections tab still has the book",
        ).toEqual([bookFolder]);
    });

    expect(
        bloomApp.takeNonFatalProblems(),
        "Bloom reported a problem while the book took the title's name",
    ).toEqual([]);

    // The collection attached to BL-12587 stopped Bloom from starting at all.
    await step(
        "Restart Bloom on the collection and edit the book",
        async () => {
            const restartedPage = await bloomApp.restart();
            await editBook(restartedPage, bookFolder);
            // Bloom shows the page that the book was last on.
            await selectPage(restartedPage, coverId);
            await expect(
                await clickInGroup(restartedPage, ".bookTitle", "en"),
                "the cover's title after a restart",
            ).toHaveText(kTitle, { timeout: 15000, useInnerText: true });
        },
    );

    expect(
        bloomApp.takeNonFatalProblems(),
        "Bloom reported a problem when it started on the book and opened it",
    ).toEqual([]);
});
