// A real book from Bloom Library survives a trip through a spreadsheet: exported with More > Export
// to Spreadsheet..., then imported with More > Import Content from Spreadsheet... into a new blank
// book, which ends up with the same pages, text in every language, pictures and recorded narration,
// and the import reports no problem.
//
// Automates the manual test "Spreadsheet Export Import Roundtrip" (Test Case ID 626).
//
// The case uses Chetana's "My Beach" (https://bloomlibrary.org/chetana/book/IwqAGQAkVI), a
// Malayalam and English book whose narration is recorded by text box with sentence timings: the
// book that showed BL-16263, an import that refused its timings. The test uses it too, fetched from
// Bloom Library when the test runs, so the test needs Bloom Library to be reachable. A person
// downloads it with the website's "Download into Bloom Editor" button, a trip through the system
// browser that a test cannot drive; the test copies the book's files the way Bloom's download does
// (helpers/bloomLibraryDownload.ts) and makes its book from that copy, as the case does.
//
// Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to the
// front, under --dont-disturb too, so the test does not run where it would take a developer's
// keyboard; see "Export to Spreadsheet opens the spreadsheet" in AUTOMATION-DEBT.md.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { downloadBookFromBloomLibrary } from "../helpers/bloomLibraryDownload";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    makeBookFromBookInCollection,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    exportSelectedBookToSpreadsheet,
    importSpreadsheetIntoSelectedBook,
} from "../helpers/spreadsheet";
import { switchTab } from "../helpers/workspace";

/** Chetana's "My Beach" on Bloom Library: https://bloomlibrary.org/chetana/book/IwqAGQAkVI */
const kBloomLibraryBookId = "IwqAGQAkVI";

test.use({
    collectionSpec: {
        name: "spreadsheet-roundtrip",
        // As in BL-16263: a Malayalam collection, so the book made from the download is in
        // Malayalam, with English as the second language.
        languages: ["ml", "en"],
        // Spreadsheet export and import need a Local Community subscription or better.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** One text box of a page as the saved book has it, in one language. */
interface ITextOnDisk {
    lang: string;
    text: string;
    /** "TextBox" or "Sentence" when the box is recorded, "" when it is not. */
    recordingMode: string;
    /**
     * The ends of the recorded sentences, in seconds, for a box recorded as a whole
     * (data-audiorecordingendtimes), as written.
     */
    sentenceEnds: string[];
    /** The ids of the recordings that play this box's text: the box's own, or its sentences'. */
    recordingIds: string[];
}

/** One content page as the saved book has it. */
interface IPageOnDisk {
    texts: ITextOnDisk[];
    /** The file names of the page's pictures, in order. */
    pictures: string[];
}

/**
 * The content pages of the book in `bookFolder` (front and back matter left out), as its saved
 * file has them. `page` is used only as a DOM parser.
 */
async function readContentPages(
    page: Page,
    bookFolder: string,
): Promise<IPageOnDisk[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        const pages = Array.from(
            dom.querySelectorAll(
                ".bloom-page:not(.bloom-frontMatter):not(.bloom-backMatter)",
            ),
        );
        return pages.map((p) => ({
            texts: Array.from(
                p.querySelectorAll<HTMLElement>(
                    ".bloom-translationGroup .bloom-editable[lang]",
                ),
            )
                // "z" is the template box Bloom copies for each language; "*" and "" hold no
                // text of the book.
                .filter((box) => !["z", "*", ""].includes(box.lang))
                .map((box) => {
                    const recordingMode =
                        box.getAttribute("data-audiorecordingmode") ?? "";
                    const recorded =
                        recordingMode === "TextBox"
                            ? [box]
                            : Array.from(
                                  box.querySelectorAll<HTMLElement>(
                                      ".audio-sentence",
                                  ),
                              );
                    return {
                        lang: box.lang,
                        text: (box.textContent ?? "")
                            .replace(/\s+/g, " ")
                            .trim(),
                        recordingMode,
                        sentenceEnds: (
                            box.getAttribute("data-audiorecordingendtimes") ??
                            ""
                        )
                            .split(" ")
                            .filter((end) => end),
                        recordingIds: recorded
                            .filter((element) =>
                                element.classList.contains("audio-sentence"),
                            )
                            .map((element) => element.id),
                    };
                })
                .filter((text) => text.text !== ""),
            pictures: Array.from(
                p.querySelectorAll<HTMLImageElement>(
                    ".bloom-canvas img, .bloom-imageContainer img",
                ),
            ).map((img) => decodeURIComponent(img.getAttribute("src") ?? "")),
        }));
    }, html);
}

/** The folder of the book whose meta.json has this bookInstanceId, among the collection's. */
function findBookFolderById(collectionDir: string, bookId: string): string {
    const folders = fs
        .readdirSync(collectionDir, { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => Path.join(collectionDir, entry.name))
        .filter((folder) => fs.existsSync(Path.join(folder, "meta.json")));
    const match = folders.find(
        (folder) =>
            JSON.parse(fs.readFileSync(Path.join(folder, "meta.json"), "utf8"))
                .bookInstanceId === bookId,
    );
    if (!match)
        throw new Error(
            `No book in ${collectionDir} has the id ${bookId}. Folders: ` +
                folders.map((f) => Path.basename(f)).join(", "),
        );
    return match;
}

/** The bookInstanceId in the meta.json of the book in `bookFolder`. */
function readBookId(bookFolder: string): string {
    return JSON.parse(
        fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
    ).bookInstanceId;
}

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

test("A Bloom Library book's content survives export to a spreadsheet and import into a new book [Test Case ID 626]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);

    const downloaded = await step(
        "Download the book from Bloom Library into the collection",
        async () => {
            let folder = "";
            await bloomApp.restart(async () => {
                folder = await downloadBookFromBloomLibrary(
                    kBloomLibraryBookId,
                    bloomApp.collectionDir,
                );
            });
            return folder;
        },
    );
    if (!downloaded) throw new Error("The book was not downloaded.");
    const page = bloomApp.page;

    const original = await step("Make a book from it", async () => {
        await switchTab(page, "collection");
        return makeBookFromBookInCollection(page, downloaded);
    });
    if (!original) throw new Error("No book was made from the download.");
    const originalPages = await readContentPages(page, original);

    await step("Check the book has narration recorded by text box", () => {
        // Without this the test would not cover what BL-16263 broke.
        const boxes = originalPages.flatMap((p) => p.texts);
        expect(
            boxes.filter(
                (box) =>
                    box.recordingMode === "TextBox" &&
                    box.sentenceEnds.length > 1,
            ).length,
            "the downloaded book should have text boxes recorded as a whole, with sentence timings",
        ).toBeGreaterThan(0);
    });

    const exportFolder = testInfo.outputPath("spreadsheets");
    fs.mkdirSync(exportFolder, { recursive: true });
    const spreadsheet = await step("Export it to a spreadsheet", async () => {
        await switchTab(page, "collection");
        await selectBook(page, original);
        return exportSelectedBookToSpreadsheet(page, exportFolder);
    });
    if (!spreadsheet) throw new Error("The book was not exported.");

    const newBookId = await step("Make a new blank book", async () => {
        await switchTab(page, "collection");
        return readBookId(await makeBookFromTemplate(page, "Basic Book"));
    });
    if (!newBookId) throw new Error("No new book was made.");

    const progress = await step(
        "Import the spreadsheet into the new book",
        async () => {
            await switchTab(page, "collection");
            await selectBook(
                page,
                findBookFolderById(bloomApp.collectionDir, newBookId),
            );
            return importSpreadsheetIntoSelectedBook(page, spreadsheet);
        },
    );
    if (!progress) throw new Error("The spreadsheet was not imported.");

    await step("Check the import reported no problem", () => {
        expect(
            progress.errors,
            `The import said:\n${progress.messages.join("\n")}`,
        ).toEqual([]);
        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem during the round trip",
        ).toEqual([]);
    });

    await step(
        "Check the new book has the original's pages, text, pictures and narration",
        async () => {
            const imported = findBookFolderById(
                bloomApp.collectionDir,
                newBookId,
            );
            const importedPages = await readContentPages(page, imported);
            // The import keeps every sentence end the spreadsheet gives but the last, which it
            // sets to the length it measures of the recording (SpreadsheetImporter.cs, "Set the
            // end times"). That differs a little from the length the recorder wrote, so the last
            // end is compared to within a quarter of a second, and everything else exactly.
            const withoutLastEnds = (pages: IPageOnDisk[]) =>
                pages.map((p) => ({
                    ...p,
                    texts: p.texts.map((t) => ({
                        ...t,
                        sentenceEnds: t.sentenceEnds.slice(0, -1),
                    })),
                }));
            expect(withoutLastEnds(importedPages)).toEqual(
                withoutLastEnds(originalPages),
            );
            const lastEnds = (pages: IPageOnDisk[]) =>
                pages.flatMap((p) =>
                    p.texts.map((t) => Number(t.sentenceEnds.at(-1) ?? 0)),
                );
            const importedLastEnds = lastEnds(importedPages);
            lastEnds(originalPages).forEach((end, index) => {
                expect(
                    Math.abs(importedLastEnds[index] - end),
                    `text box ${index} should end its recording where the original does, ` +
                        `${end} s, not ${importedLastEnds[index]} s`,
                ).toBeLessThan(0.25);
            });
            for (const id of importedPages.flatMap((p) =>
                p.texts.flatMap((t) => t.recordingIds),
            )) {
                expect(
                    fs.existsSync(Path.join(imported, "audio", `${id}.mp3`)),
                    `the new book should have the recording ${id}.mp3`,
                ).toBe(true);
            }
            for (const picture of importedPages.flatMap((p) => p.pictures)) {
                expect(
                    fs.existsSync(Path.join(imported, picture)),
                    `the new book should have the picture ${picture}`,
                ).toBe(true);
            }
        },
    );
});
