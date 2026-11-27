// Duplicate Book on the Collections tab, right after making a book: the copy's meta.json must say
// everything the original's says, in the same JSON types, with only a new bookInstanceId. Bloom once
// wrote a duplicate's 0 as 0.0 and its [] as {} or null, which made the copy's meta.json unreadable
// (BL-9956). Then the copy must open in the Edit tab without Bloom reporting a problem.
//
// Automates the manual test "Duplicate Book Without Errors" (Test Case ID 82).

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    editablePageFrame,
    makeBookFromTemplate,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    chooseFromSelectedBookMenuByTestId,
    clickBookTile,
    findBookFolderById,
    getSelectedBookId,
    listEditableBooks,
} from "../helpers/collection";
import {
    enableToolForBook,
    getShownTools,
    isToolboxShowing,
    showToolbox,
} from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "duplicate-book", languages: ["en"] },
});

/** What a number literal becomes in parseJsonKeepingNumberText's result. */
interface INumberText {
    numberText: string;
}

/**
 * Parse JSON text, keeping each number as the text it was written with ({numberText: "0"}), so that
 * 0 and 0.0 differ, as they do to the C# reader that maps a number onto an enum. Arrays, objects
 * and null stay what JSON.parse makes them, so [] and {} and null differ too.
 */
function parseJsonKeepingNumberText(text: string): unknown {
    // Wrap every number literal outside a string in a marked string, then let JSON.parse do the rest.
    const marker = "\u0001number:";
    let marked = "";
    let i = 0;
    while (i < text.length) {
        const c = text[i];
        if (c === '"') {
            let end = i + 1;
            while (text[end] !== '"') {
                if (end >= text.length)
                    throw new Error(`a string in the JSON never ends: ${text}`);
                end += text[end] === "\\" ? 2 : 1;
            }
            marked += text.slice(i, end + 1);
            i = end + 1;
        } else if (c === "-" || (c >= "0" && c <= "9")) {
            const literal = /^-?\d+(\.\d+)?([eE][+-]?\d+)?/.exec(
                text.slice(i),
            )![0];
            marked += JSON.stringify(marker + literal);
            i += literal.length;
        } else {
            marked += c;
            i++;
        }
    }
    return JSON.parse(marked, (_key, value) =>
        typeof value === "string" && value.startsWith(marker)
            ? ({ numberText: value.slice(marker.length) } as INumberText)
            : value,
    );
}

/** The meta.json of the book in `bookFolder`, as parseJsonKeepingNumberText reads it. */
function readMetaKeepingNumberText(
    bookFolder: string,
): Record<string, unknown> {
    const text = fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8");
    return parseJsonKeepingNumberText(text) as Record<string, unknown>;
}

/** What a copy's meta.json must share with its original's: everything but the book's id. */
function comparableMeta(
    meta: Record<string, unknown>,
): Record<string, unknown> {
    const { bookInstanceId: _id, ...rest } = meta;
    return rest;
}

/** The values in `meta` (and in its objects and arrays) that pass `test`. */
function findValues(
    meta: unknown,
    test: (value: unknown) => boolean,
): unknown[] {
    const found: unknown[] = [];
    const visit = (value: unknown) => {
        if (test(value)) found.push(value);
        if (value && typeof value === "object")
            Object.values(value).forEach(visit);
    };
    visit(meta);
    return found;
}

test("Duplicate Book right after making a book copies its meta.json exactly and opens without errors [Test Case ID 82]", async ({
    page,
    bloomApp,
    step,
}) => {
    let originalId = "";
    let originalFolder = "";
    await step(
        "Make a book, turn a tool on, and go straight to the Collections tab",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            // A new book's meta.json holds only default values, and Bloom replaces a meta.json it
            // cannot read with one of default values and the book's id, so a copy that Bloom could not
            // read would pass for a good one. A tool turned on and the toolbox left open are things
            // only meta.json keeps, so they make the copy show whether Bloom read what was copied.
            await enableToolForBook(page, folder, "impairmentVisualizer");
            await showToolbox(page);
            await switchTab(page, "collection");
            const books = await listEditableBooks(page);
            expect(
                books.length,
                "Sanity check: the collection should hold only the book just made",
            ).toBe(1);
            originalId = books[0].id;
            originalFolder = findBookFolderById(
                bloomApp.collectionDir,
                originalId,
            );
        },
    );

    let originalMeta: Record<string, unknown> = {};
    await step("Choose Duplicate Book from the book's menu", async () => {
        await clickBookTile(page, originalId);
        originalMeta = readMetaKeepingNumberText(originalFolder);
        expect(
            originalMeta.toolboxIsOpen,
            "Sanity check: the new book's meta.json should say its toolbox was left open",
        ).toBe(true);
        expect(
            JSON.stringify(originalMeta.tools),
            "Sanity check: the new book's meta.json should list the tool turned on",
        ).toContain("impairmentVisualizer");
        // Without whole numbers and empty arrays in the original, the type checks below prove nothing.
        expect(
            findValues(
                originalMeta,
                (v) =>
                    typeof v === "object" &&
                    v !== null &&
                    /^-?\d+$/.test((v as INumberText).numberText ?? ""),
            ).length,
            "Sanity check: the new book's meta.json should hold whole numbers, such as 0",
        ).toBeGreaterThan(0);
        expect(
            findValues(originalMeta, (v) => Array.isArray(v) && v.length === 0)
                .length,
            "Sanity check: the new book's meta.json should hold empty lists ([])",
        ).toBeGreaterThan(0);
        expect(
            bloomApp.takeNonFatalProblems(),
            "Sanity check: Bloom should have reported no problem before the duplicate",
        ).toEqual([]);
        await chooseFromSelectedBookMenuByTestId(
            page,
            "CollectionTab.BookMenu.DuplicateBook",
        );
    });

    let copyId = "";
    let copyFolder = "";
    /**
     * Wait until the copy's meta.json says what the original's said apart from the id, and fail
     * with `message` and the difference when it never does. It is read whole each time, so a
     * reading taken while Bloom is writing it is retried rather than failed.
     */
    const expectCopyMetaLikeOriginal = async (message: string) => {
        await expect
            .poll(
                () => {
                    try {
                        return comparableMeta(
                            readMetaKeepingNumberText(copyFolder),
                        );
                    } catch (e) {
                        return `meta.json could not be read: ${e}`;
                    }
                },
                { timeout: 15000, message },
            )
            .toEqual(comparableMeta(originalMeta));
    };
    await step("See the copy in the collection, selected", async () => {
        await expect
            .poll(async () => (await listEditableBooks(page)).length, {
                timeout: 30000,
                message: "Duplicate Book should add one book to the collection",
            })
            .toBe(2);
        copyId = (await listEditableBooks(page)).find(
            (b) => b.id !== originalId,
        )!.id;
        copyFolder = findBookFolderById(bloomApp.collectionDir, copyId);
        expect(
            Path.basename(copyFolder),
            "the copy's folder should be named for the original's, with - Copy- and part of its id",
        ).toMatch(
            new RegExp(
                `^${Path.basename(originalFolder).replace(/[.*+?^${}()|[\]\\]/g, "\\$&")} - Copy-[0-9a-f]+$`,
            ),
        );
        await expect
            .poll(() => getSelectedBookId(page), {
                timeout: 30000,
                message: "Bloom should select the copy once it is made",
            })
            .toBe(copyId);
        await expect(
            page.locator(`.book-button[data-book-id="${copyId}"]`),
            "the copy should have a tile on the Collections tab",
        ).toBeVisible({ timeout: 30000 });
    });

    await step("Examine the copy's meta.json", async () => {
        expect(
            copyId,
            "the copy should have a bookInstanceId of its own",
        ).not.toBe(originalId);
        expect(
            readMetaKeepingNumberText(copyFolder).bookInstanceId,
            "the copy's meta.json should hold the copy's own id",
        ).toBe(copyId);
        // Bloom writes the copy's meta.json twice: Duplicate Book copies the original's and puts the
        // new id in, then Bloom reads the copy and saves it again a moment later. This can see
        // either; the check after the copy opens in the Edit tab sees what Bloom made of it.
        await expectCopyMetaLikeOriginal(
            "apart from its id, the copy's meta.json should say what the original's says, with each number written the same and no [] turned into {} or null",
        );
        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem while duplicating the book",
        ).toEqual([]);
    });

    await step("Open the copy in the Edit tab", async () => {
        await switchTab(page, "edit");
        await waitForEditablePage(page);
        // The page frame's address is the page's file in its book's folder. The copy's pages
        // have the same ids as the original's, so the folder is what tells the two apart.
        expect(
            decodeURIComponent(editablePageFrame(page).url()).replace(
                /\\/g,
                "/",
            ),
            "the Edit tab should be showing a page of the copy",
        ).toContain(`/${Path.basename(copyFolder)}/`);
        // By now Bloom has read the copy's meta.json to open it, and saved what it read.
        await expectCopyMetaLikeOriginal(
            "once the copy is open in the Edit tab, its meta.json should still say what the original's says, apart from its id: Bloom should have read everything that was copied",
        );
        // And the Edit tab shows the copy the way its meta.json says: toolbox open, tool offered.
        await expect
            .poll(() => isToolboxShowing(page), {
                timeout: 30000,
                message:
                    "the copy should open with its toolbox open, as the original was left",
            })
            .toBe(true);
        await expect
            .poll(() => getShownTools(page), {
                timeout: 30000,
                message:
                    "the copy's toolbox should offer the tool turned on in the original",
            })
            .toContain("impairmentVisualizer");
        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem while opening the copy",
        ).toEqual([]);
    });
});
