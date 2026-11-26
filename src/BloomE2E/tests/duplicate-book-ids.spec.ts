// When a person copies a book's folder with File Explorer, the copy carries the same book ID
// (bookInstanceId in meta.json) as the book it came from. Bloom looks for such duplicates each time
// it opens a collection and gives all but one of each set a new ID. In a Team Collection it gives
// the new IDs to the books that are not in the shared folder, even when the shared book is the
// newer one, and whether or not it is checked out (BL-10512).
//
// Each test copies the folder while Bloom is closed, keeping the files' times as File Explorer
// does, restarts Bloom, and reads every book's meta.json.
//
// Automates the manual test "Correct Duplicate Book IDs" (Test Case ID 664).

import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { readZip } from "../helpers/bulkBloomPub";
import {
    clickBookTile,
    listEditableBooks,
    readBookId,
    waitForCollectionReady,
} from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    checkOutSelectedBook,
    createTeamCollection,
    isSelectedBookSaveable,
} from "../helpers/teamCollection";
import { switchTab } from "../helpers/workspace";

// Team Collections need a tier above Basic and their experimental feature.
test.use({
    collectionSpec: {
        name: "duplicate-book-ids",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["team-collections"],
});

/**
 * Copy a book folder beside itself as File Explorer does: every file, with its times kept, into
 * `copyName`, such as "<name> - Copy" or "<name> - Copy (2)".
 */
function copyBookFolderLikeExplorer(bookFolder: string, copyName: string) {
    const copy = Path.join(Path.dirname(bookFolder), copyName);
    expect(
        fs.existsSync(copy),
        `Sanity check: there is no ${copyName} folder yet`,
    ).toBe(false);
    fs.cpSync(bookFolder, copy, { recursive: true, preserveTimestamps: true });
    expect(
        readBookId(copy),
        "Sanity check: the copy has the original's ID before Bloom sees it",
    ).toBe(readBookId(bookFolder));
}

/** Give a file a last-write time `minutes` later than now. */
function touchLater(file: string, minutes: number) {
    const later = new Date(Date.now() + minutes * 60000);
    fs.utimesSync(file, later, later);
}

/**
 * The book ID in the meta.json of `original` and of each copy of it, by folder name. Copies are
 * the folders beside it whose names start with its name and " - Copy". A Team Collection renames
 * a copy's folder as it starts (it drops the parentheses of "Copy (2)"), so they are found afresh
 * each time.
 */
function readIdsOfBookAndCopies(original: string) {
    const name = Path.basename(original);
    const ids: Record<string, string> = { [name]: readBookId(original) };
    for (const entry of fs.readdirSync(Path.dirname(original), {
        withFileTypes: true,
    })) {
        if (entry.isDirectory() && entry.name.startsWith(`${name} - Copy`))
            ids[entry.name] = readBookId(
                Path.join(entry.parentPath, entry.name),
            );
    }
    return ids;
}

/**
 * Check that `original` and its `copyCount` copies each have an ID of their own, that the original
 * kept `originalId` when `originalKeepsId` (otherwise only that exactly one of them kept it), and
 * that Bloom lists each with the ID its meta.json holds.
 */
async function expectEachBookHasItsOwnId(
    page: Page,
    original: string,
    originalId: string,
    copyCount: number,
    originalKeepsId: boolean,
) {
    // Bloom repairs duplicate IDs, and a Team Collection renames folders, before it says the
    // collection is ready.
    await waitForCollectionReady(page);
    const ids = readIdsOfBookAndCopies(original);
    const folders = Object.keys(ids);
    expect(
        folders,
        `Sanity check: the book and its ${copyCount} copies are all there`,
    ).toHaveLength(copyCount + 1);
    expect(
        new Set(Object.values(ids)).size,
        `each book should end with an ID of its own, but they have ${JSON.stringify(ids)}`,
    ).toBe(copyCount + 1);
    if (originalKeepsId) {
        expect(
            ids[Path.basename(original)],
            "the book that was copied should keep its ID",
        ).toBe(originalId);
    } else {
        expect(
            Object.values(ids).filter((id) => id === originalId),
            "exactly one of the books should keep the original's ID",
        ).toHaveLength(1);
    }
    const listed = await listEditableBooks(page);
    for (const folder of folders) {
        expect(
            listed.find((book) => book.id === ids[folder]),
            `Bloom should list ${folder} with the ID its meta.json holds`,
        ).toBeDefined();
    }
}

/** The book ID in the meta.json of the book `bookFolderName` in a Team Collection's shared folder. */
function readSharedBookId(sharedFolder: string, bookFolderName: string) {
    const books = fs
        .readdirSync(sharedFolder, { recursive: true, encoding: "utf8" })
        .filter(
            (file) =>
                Path.basename(file) === `${bookFolderName}.bloom` &&
                Path.basename(Path.dirname(file)) === "Books",
        );
    expect(
        books,
        `the shared folder should hold ${bookFolderName}.bloom once`,
    ).toHaveLength(1);
    const meta = readZip(fs.readFileSync(Path.join(sharedFolder, books[0])))
        .filter((entry) => Path.basename(entry.name) === "meta.json")
        .map((entry) => JSON.parse(entry.content.toString("utf8")));
    expect(
        meta,
        `${bookFolderName}.bloom should hold one meta.json`,
    ).toHaveLength(1);
    return meta[0].bookInstanceId as string;
}

test("in a plain collection, Bloom gives copies of a book made with File Explorer new IDs [Test Case ID 664]", async ({
    page,
    bloomApp,
    step,
}) => {
    let original = "";
    let originalId = "";
    await step("Make a book", async () => {
        original = await makeBookFromTemplate(page, "Basic Book");
        await switchTab(page, "collection");
        originalId = readBookId(original);
    });
    const name = Path.basename(original);

    let restarted = page;
    await step(
        "Copy its folder twice while Bloom is closed, then start Bloom",
        async () => {
            restarted = await bloomApp.restart(() => {
                copyBookFolderLikeExplorer(original, `${name} - Copy`);
                copyBookFolderLikeExplorer(original, `${name} - Copy (2)`);
            });
        },
    );

    // Explorer keeps the copies' times, so all three are equally old, and outside a Team
    // Collection Bloom may keep the ID on any one of them.
    await step("Every book has an ID of its own", async () => {
        await expectEachBookHasItsOwnId(
            restarted,
            original,
            originalId,
            2,
            false,
        );
    });
});

test("in a Team Collection, Bloom gives new IDs to copies of a shared book, not to the shared book, checked in or out [Test Case ID 664]", async ({
    page,
    bloomApp,
    step,
}) => {
    const sharedFolder = fs.mkdtempSync(
        Path.join(os.tmpdir(), "bloom-e2e-tc-duplicate-ids-"),
    );
    try {
        let original = "";
        let originalId = "";
        await step("Make a book", async () => {
            original = await makeBookFromTemplate(page, "Basic Book");
            await switchTab(page, "collection");
            originalId = readBookId(original);
        });
        const name = Path.basename(original);

        let tcPage = page;
        await step(
            "Make the collection a Team Collection, which checks the book in",
            async () => {
                tcPage = await createTeamCollection(bloomApp, sharedFolder);
                expect(
                    readSharedBookId(sharedFolder, name),
                    "Sanity check: the shared folder holds the book with its ID",
                ).toBe(originalId);
            },
        );

        await step(
            "While Bloom is closed, copy the checked-in book twice, then make the checked-in book the newer one",
            async () => {
                tcPage = await bloomApp.restart(() => {
                    copyBookFolderLikeExplorer(original, `${name} - Copy`);
                    copyBookFolderLikeExplorer(original, `${name} - Copy (2)`);
                    // Outside a Team Collection Bloom keeps the ID of the book whose meta.json is
                    // oldest, so this makes that rule pick the shared book to change.
                    touchLater(Path.join(original, "meta.json"), 5);
                });
            },
        );

        await step(
            "The copies get new IDs; the checked-in book keeps its ID, here and in the shared folder",
            async () => {
                await expectEachBookHasItsOwnId(
                    tcPage,
                    original,
                    originalId,
                    2,
                    true,
                );
                expect(
                    readSharedBookId(sharedFolder, name),
                    "the shared folder's copy of the book should keep its ID",
                ).toBe(originalId);
            },
        );

        await step("Check the shared book out", async () => {
            await clickBookTile(tcPage, originalId);
            await checkOutSelectedBook(tcPage);
        });

        await step(
            "While Bloom is closed, copy the checked-out book, then make it the newer one",
            async () => {
                tcPage = await bloomApp.restart(() => {
                    copyBookFolderLikeExplorer(original, `${name} - Copy (3)`);
                    touchLater(Path.join(original, "meta.json"), 10);
                });
            },
        );

        await step(
            "The copy gets a new ID; the checked-out book keeps its ID and stays checked out",
            async () => {
                await expectEachBookHasItsOwnId(
                    tcPage,
                    original,
                    originalId,
                    3,
                    true,
                );
                expect(
                    readSharedBookId(sharedFolder, name),
                    "the shared folder's copy of the book should keep its ID",
                ).toBe(originalId);
                await clickBookTile(tcPage, originalId);
                expect(
                    await isSelectedBookSaveable(tcPage),
                    "the book should still be checked out to me",
                ).toBe(true);
            },
        );
    } finally {
        fs.rmSync(sharedFolder, { recursive: true, force: true });
    }
});
