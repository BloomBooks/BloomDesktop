// Bloom reopens the book that was selected when it last closed. If that book has lost both its .htm
// and its bookhtml.bak backup, there is nothing to open it from, and Bloom must still start: it
// skips the book, forgets it, and shows the collection. Automates the manual test "No Crash Without
// Backups" (BL-11678 was this crash).
//
// The test makes two books, selects the one it will break, stops Bloom, deletes that book's .htm
// and bookhtml.bak, and starts Bloom again. The background watcher fails the test if Bloom shows
// its problem dialog at any point.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { apiGet, apiGetJson } from "../helpers/api";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import { selectBook, waitForCollectionReady } from "../helpers/collection";
import { readSavedUserSetting } from "../helpers/userSettings";
import { getTabs, switchTab, waitForActiveTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "no-crash-without-backups", languages: ["en"] },
});

// Bloom's name for a book's backup of its .htm (BookStorage.BackupFilename).
const BACKUP_FILE = "bookhtml.bak";

/** One book as collections/books reports it. Only the field used here. */
interface IBookInfo {
    folderPath: string;
}

/** The folders of the books in the collection Bloom has open. */
async function listBookFolders(page: Page): Promise<string[]> {
    const name = (
        await apiGet(page, "collections/getCurrentEditableCollectionName")
    ).body;
    const collections = await apiGetJson<{ id: string; name: string }[]>(
        page,
        "collections/list",
    );
    const editable = collections.find((c) => c.name === name);
    expect(
        editable,
        `collections/list has no collection called "${name}"`,
    ).toBeDefined();
    const books = await apiGetJson<IBookInfo[]>(
        page,
        `collections/books?collection-id=${encodeURIComponent(editable!.id)}`,
    );
    return books.map((b) => normalized(b.folderPath));
}

/** A folder path in a form that compares the way Windows compares paths. */
function normalized(folder: string): string {
    return Path.resolve(folder).toLowerCase();
}

/**
 * The book Bloom has saved as the one to reopen at its next start, normalized; "" when it has
 * saved none.
 */
async function readBookToReopen(userSettingsDir: string): Promise<string> {
    const saved = await readSavedUserSetting(
        userSettingsDir,
        "CurrentBookPath",
    );
    const path = saved?.trim();
    return path ? normalized(path) : "";
}

/** The .htm files in a book folder. */
function htmFilesIn(folder: string): string[] {
    return fs.readdirSync(folder).filter((f) => /\.html?$/i.test(f));
}

/** Whether Windows still has a process with this id. */
function isRunning(pid: number): boolean {
    try {
        process.kill(pid, 0);
        return true;
    } catch {
        return false;
    }
}

test("Bloom starts without crashing when the book it reopens has lost its .htm and its backup [Test Case ID 34]", async ({
    page,
    bloomApp,
    step,
}) => {
    test.setTimeout(300000);

    const books = await step(
        "Make two books and select the one to break",
        async () => {
            const goodBook = await makeBookFromTemplate(page, "Basic Book");
            const brokenBook = await makeBookFromTemplate(page, "Basic Book");
            await switchTab(page, "collection");
            await selectBook(page, brokenBook);
            return { goodBook, brokenBook };
        },
    );
    if (!books) throw new Error("The books were not made.");
    const { goodBook, brokenBook } = books;

    await step(
        "Check Bloom will reopen the book and the book has a backup",
        async () => {
            // Bloom remembers the selected book in its user settings; that is what makes it try to
            // reopen the broken book at the next start.
            await expect
                .poll(() => readBookToReopen(bloomApp.userSettingsDir), {
                    timeout: 15000,
                    message:
                        "Bloom never saved the selected book as the one to reopen",
                })
                .toBe(normalized(brokenBook));
            expect(
                htmFilesIn(brokenBook),
                "test setup: the book to break has one .htm",
            ).toHaveLength(1);
            expect(
                fs.existsSync(Path.join(brokenBook, BACKUP_FILE)),
                `test setup: Bloom did not make ${BACKUP_FILE} for the book to break, so deleting it tests nothing`,
            ).toBe(true);
        },
    );

    const restarted = await step(
        "Close Bloom, delete the book's .htm and backup, and start Bloom again",
        () =>
            bloomApp.restart(() => {
                for (const htm of htmFilesIn(brokenBook))
                    fs.rmSync(Path.join(brokenBook, htm));
                fs.rmSync(Path.join(brokenBook, BACKUP_FILE));
                expect(
                    htmFilesIn(brokenBook),
                    "the .htm is still there",
                ).toHaveLength(0);
                expect(
                    fs.existsSync(Path.join(brokenBook, BACKUP_FILE)),
                    `${BACKUP_FILE} is still there`,
                ).toBe(false);
            }),
    );
    if (!restarted) throw new Error("Bloom did not restart.");

    await step("Check Bloom started and shows the collection", async () => {
        await waitForCollectionReady(restarted);
        await waitForActiveTab(restarted, "collection");
        expect(isRunning(bloomApp.bloomPid), "Bloom's process is gone").toBe(
            true,
        );
        // Nothing could be reopened, so no book is selected, and Bloom hides the Edit tab.
        expect((await getTabs(restarted)).tabStates.edit).toBe("hidden");
        const listed = await listBookFolders(restarted);
        expect(
            listed,
            "the good book is missing from the collection",
        ).toContain(normalized(goodBook));
        expect(
            listed,
            "the book with no .htm and no backup is still listed",
        ).not.toContain(normalized(brokenBook));
    });

    await step("Check Bloom forgot the broken book", async () => {
        // Otherwise it would meet the same book at every start.
        await expect
            .poll(() => readBookToReopen(bloomApp.userSettingsDir), {
                timeout: 15000,
                message:
                    "Bloom still means to reopen the book it could not open",
            })
            .toBe("");
    });

    await step("Open the good book", async () => {
        await selectBook(restarted, goodBook);
        await switchTab(restarted, "edit");
    });
});
