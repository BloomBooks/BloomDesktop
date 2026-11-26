// Test Case ID 393, "Illegal Filename Characters".
//
// A book's title names its folder and file, and a collection's name names its folder and settings
// file. Windows refuses some characters in a file name (? : < > " and others), and some that it
// allows still trouble Bloom's server, JSON or bloom-player (& ' { }). The case types such
// characters into a title and into a collection's name, and asks that Bloom handle them gracefully:
// it may refuse them or put something safe in the file names, but it must carry on working, and
// what the person typed must still be what the book or collection shows.
//
// Under --e2e Bloom shows a NonFatalProblem to nobody and writes it to stderr, so the book test
// reads those with bloomApp.takeNonFatalProblems(). The fixture itself fails a test when Bloom shows
// its "Bloom had a problem" dialog.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    clickInGroup,
    editBook,
    getPages,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    clickBookTile,
    expectBookInPreview,
    listEditableBooks,
    waitForCollectionReady,
} from "../helpers/collection";
import {
    cancelCollectionSettings,
    describeCollectionFolder,
    getCollectionSettingsOkLabel,
    getCollectionSettingsText,
    openCollectionSettings,
    setCollectionSettingsText,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import { selectPage } from "../helpers/pageThumbnails";
import { realClick } from "../helpers/realClick";
import { switchTab } from "../helpers/workspace";

// The characters the case names.
const kCaseCharacters = [..."?:&<>'\"{}"];

// Characters that Windows refuses anywhere in a file or folder name.
const kWindowsRefuses = /[<>:"/\\|?*\x00-\x1f]/;

const kTitle = `Who? Why: A&B <Tag> 'Q' "Say" {Set}`;
// A second book, made in the renamed collection, needs a title of its own.
const kSecondTitle = `Where? When: C&D <Hop> 'R' "Ask" {Bag}`;
const kCollectionName = `Our? Books: <A&B> 'C' "D" {E}`;

const samePath = (a: string, b: string) =>
    Path.resolve(a).toLowerCase() === Path.resolve(b).toLowerCase();

/** The words of a name, which a safe version of it must keep, in order. */
const words = (name: string) => name.match(/[A-Za-z]+/g) ?? [];

/** Make a book from the Basic Book template; return its folder and its first two pages' ids. */
async function makeUntitledBook(
    page: Page,
): Promise<{ oldFolder: string; coverId: string; otherPageId: string }> {
    const oldFolder = await makeBookFromTemplate(page, "Basic Book");
    // Not the placeholder's name itself ("Book" in English), which Bloom localizes.
    expect(
        fs.existsSync(oldFolder),
        "test setup: the new book has a folder of its own",
    ).toBe(true);
    const pages = await getPages(page);
    expect(
        pages.length,
        "test setup: the book has a page besides its cover to go to",
    ).toBeGreaterThan(1);
    return { oldFolder, coverId: pages[0].id, otherPageId: pages[1].id };
}

/**
 * Type `title` as the book's title and go to another page, which saves the book and renames its
 * folder after the title. Return the book's folder and id once the old folder is gone.
 */
async function giveTitleAndSave(
    page: Page,
    oldFolder: string,
    otherPageId: string,
    title: string,
): Promise<{ bookFolder: string; bookId: string }> {
    const otherBooks = new Set(
        (await listEditableBooks(page))
            .map((book) => book.folderPath)
            .filter((folder) => !samePath(folder, oldFolder)),
    );
    await typeInGroup(page, ".bookTitle", "en", title);
    await selectPage(page, otherPageId);
    await expect
        .poll(() => fs.existsSync(oldFolder), {
            timeout: 30000,
            message:
                "The book's folder was never renamed from its placeholder name.",
        })
        .toBe(false);
    const renamed = (await listEditableBooks(page)).filter(
        (book) => !otherBooks.has(book.folderPath),
    );
    expect(
        renamed.map((book) => book.folderPath),
        "the collection has the book under one new name",
    ).toHaveLength(1);
    return { bookFolder: renamed[0].folderPath, bookId: renamed[0].id };
}

/** Check that a book's folder and file have a safe name that still keeps the title's words. */
function expectSafeBookName(bookFolder: string, title: string) {
    const name = Path.basename(bookFolder);
    for (const character of kCaseCharacters) {
        expect(
            name.includes(character),
            `the book's folder name, ${JSON.stringify(name)}, has ${JSON.stringify(character)} in it`,
        ).toBe(false);
    }
    expect(
        words(name),
        `the book's folder name, ${JSON.stringify(name)}, keeps the title's words`,
    ).toEqual(words(title));
    expect(
        fs.existsSync(Path.join(bookFolder, name + ".htm")),
        "the book's file has the same name as its folder",
    ).toBe(true);
}

// The collection test runs last: it renames the collection, and Bloom reopens it from a new folder.
test.use({
    collectionSpec: { name: "illegal-filename-characters", languages: ["en"] },
});

test("a title with characters a file name cannot hold gives the book safe file names, and Bloom carries on [Test Case ID 393]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);
    for (const character of kCaseCharacters) {
        expect(
            kTitle.includes(character),
            `test setup: the title has ${JSON.stringify(character)}`,
        ).toBe(true);
    }

    let oldFolder = "";
    let coverId = "";
    let otherPageId = "";
    await step("Make a book from the Basic Book template", async () => {
        ({ oldFolder, coverId, otherPageId } = await makeUntitledBook(page));
    });
    // Whatever Bloom reported while it made the book is not this test's subject.
    bloomApp.takeNonFatalProblems();

    let bookFolder = "";
    let bookId = "";
    await step(
        "Type the title on the cover and go to another page, so that the book's files take its name",
        async () => {
            ({ bookFolder, bookId } = await giveTitleAndSave(
                page,
                oldFolder,
                otherPageId,
                kTitle,
            ));
        },
    );

    await step("Check the name that the book's files got", () => {
        expectSafeBookName(bookFolder, kTitle);
    });

    await step("Return to the Collections tab", async () => {
        await switchTab(page, "collection");
        await clickBookTile(page, bookId);
        // The book still shows the title as typed, not its file name.
        await expectBookInPreview(page, kTitle);
    });

    expect(
        bloomApp.takeNonFatalProblems(),
        "Bloom reported a problem while the book took the title's name",
    ).toEqual([]);

    await step(
        "Restart Bloom on the collection and edit the book",
        async () => {
            const restartedPage = await bloomApp.restart();
            await editBook(restartedPage, bookFolder);
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

test("a collection name with characters a file name cannot hold gives the collection a safe folder, and books can be made in it [Test Case ID 393]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);
    expect(
        kWindowsRefuses.test(kCollectionName),
        "test setup: the name has characters Windows refuses in a folder name",
    ).toBe(true);

    const oldDir = bloomApp.collectionDir;
    const parent = Path.dirname(oldDir);
    const before = new Set(fs.readdirSync(parent));

    await step(
        "Type the name on the Advanced page of Collection Settings",
        async () => {
            // The Settings button is on the Collections tab.
            await switchTab(page, "collection");
            await openCollectionSettings(page);
            await showCollectionSettingsPage(page, "Advanced");
            await setCollectionSettingsText(
                page,
                "Collection Name",
                kCollectionName,
            );
            await expect
                .poll(() => getCollectionSettingsOkLabel(page), {
                    message: "OK should become Restart once the name changes",
                })
                .toBe("Restart");
        },
    );

    let newDir = "";
    let reopened = page;
    await step(
        "Press Restart, and wait for Bloom to rename the folder and reopen the collection",
        async () => {
            const pageClosed = page.waitForEvent("close", {
                timeout: 60000,
            });
            pageClosed.catch(() => undefined);
            // Bloom can tear the page down before Playwright finishes its click; a "closed"
            // error means the click landed.
            await realClick(
                page
                    .getByRole("dialog")
                    .filter({ hasText: "Collection Settings" })
                    .getByRole("button", { name: "Restart", exact: true }),
            ).catch((error) => {
                if (!/closed/i.test(String(error))) throw error;
            });
            await pageClosed;

            // The copy of Bloom that does the rename chooses the new folder's name, so find
            // it as the one folder that was not there before.
            await expect
                .poll(
                    () =>
                        fs.existsSync(oldDir)
                            ? []
                            : fs
                                  .readdirSync(parent)
                                  .filter((name) => !before.has(name)),
                    {
                        timeout: 90000,
                        message:
                            "Bloom never moved the collection to one new folder.",
                    },
                )
                .toHaveLength(1);
            newDir = Path.join(
                parent,
                fs.readdirSync(parent).find((name) => !before.has(name))!,
            );
            reopened = await bloomApp.followRelaunch(newDir);
            await waitForCollectionReady(reopened);
        },
    );

    await step("Check the name that the collection's folder got", () => {
        const name = Path.basename(newDir);
        expect(
            kWindowsRefuses.test(name),
            `the collection's folder name, ${JSON.stringify(name)}, has a character Windows refuses`,
        ).toBe(false);
        expect(
            words(name),
            `the collection's folder name, ${JSON.stringify(name)}, keeps the name's words`,
        ).toEqual(words(kCollectionName));
        expect(
            describeCollectionFolder(newDir),
            "the settings file is named for the folder",
        ).toEqual({ name, settingsFiles: [`${name}.bloomCollection`] });
    });

    await step(
        "Collection Settings shows the collection's new name",
        async () => {
            await openCollectionSettings(reopened);
            await showCollectionSettingsPage(reopened, "Advanced");
            expect(
                (await getCollectionSettingsText(reopened, "Collection Name"))
                    .value,
            ).toBe(Path.basename(newDir));
            await cancelCollectionSettings(reopened);
        },
    );

    // Bloom serves a book's pages from paths under the collection's folder, so a folder name
    // with & ' { } in it must not break making, editing or renaming a book there.
    await step(
        "Make a book in the renamed collection and give it a title",
        async () => {
            expect(
                Path.basename(newDir),
                "test setup: the collection's folder keeps characters Windows allows but that can trouble Bloom's server, so the book is made under a path with them",
            ).toMatch(/[&'{}]/);
            const { oldFolder, otherPageId } = await makeUntitledBook(reopened);
            expect(
                Path.dirname(oldFolder),
                "test setup: the book is made in the renamed collection",
            ).toBe(newDir);
            const { bookFolder, bookId } = await giveTitleAndSave(
                reopened,
                oldFolder,
                otherPageId,
                kSecondTitle,
            );
            expect(Path.dirname(bookFolder)).toBe(newDir);
            expectSafeBookName(bookFolder, kSecondTitle);
            await switchTab(reopened, "collection");
            await clickBookTile(reopened, bookId);
            await expectBookInPreview(reopened, kSecondTitle);
        },
    );
});
