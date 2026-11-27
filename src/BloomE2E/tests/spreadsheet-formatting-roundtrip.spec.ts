// Text formatted with the selection toolbar keeps its formatting when its book goes through a
// spreadsheet: exported with More > Export to Spreadsheet..., then imported with More > Import
// Content from Spreadsheet... into a new blank book, whose text has the same bold, italic,
// underline, superscript and color on the same words. The text is also recorded by sentence, as
// in the case, since formatting and narration together once came through the import mangled
// (BL-12435): the new book must have the same sentences, each with its recording.
//
// Automates the manual test "Formatting Round-Trips Import" (Test Case ID 634), with the set-up
// that "Inline Formatting In Spreadsheet" (Test Case ID 633) gives it.
//
// The selection toolbar's other two buttons are not formatting the spreadsheet carries: Remove
// Format takes formatting away, and a hyperlink is not among what a spreadsheet cell holds
// (MarkedUpText.cs keeps bold, italic, underline, superscript and color only).
//
// A person records with a microphone; the test gives each sentence the Talking Book tool marked a
// recording file, as a recording would (addNarration in helpers/talkingBook.ts).
//
// Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to the
// front, under --dont-disturb too, so the test does not run where it would take a developer's
// keyboard; see "Export to Spreadsheet opens the spreadsheet" in AUTOMATION-DEBT.md.

import * as crypto from "node:crypto";
import * as fs from "node:fs";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    clickInGroup,
    editBook,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    findBookFolderById,
    readBookId,
    selectBook,
} from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseSelectionTextColor,
    getTextRunFormatting,
    selectTextIn,
    toggleSelectionFormatting,
    type SelectionToolbarButton,
} from "../helpers/selectionToolbar";
import {
    exportSelectedBookToSpreadsheet,
    importSpreadsheetIntoSelectedBook,
} from "../helpers/spreadsheet";
import {
    addNarration,
    narrationFilePath,
    openToolboxWithTalkingBook,
    writeSecondSampleNarration,
    setRecordingMode,
} from "../helpers/talkingBook";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "spreadsheet-formatting-roundtrip",
        languages: ["en"],
        // The spreadsheet commands need a Local Community subscription or better.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

const kGroup = ".bloom-translationGroup";

/** A color of the selection toolbar's text palette, as "#rrggbb". */
const kRed = "#ff1616";

/** The formatting a word has, as the book file says. */
interface IWordFormatting {
    bold: boolean;
    italic: boolean;
    underline: boolean;
    superscript: boolean;
    /** The text color the markup gives the word, as "#rrggbb", or "" when it gives none. */
    color: string;
}

/**
 * The words of the page's text box, each with the formatting it gets. No word holds another, so
 * each can be found by itself. The two sentences make two recordings.
 */
const kWords: {
    word: string;
    buttons: SelectionToolbarButton[];
    color?: string;
}[] = [
    { word: "Start", buttons: [] },
    { word: "bold", buttons: ["bold"] },
    { word: "italic", buttons: ["italic"] },
    { word: "underlined", buttons: ["underline"] },
    { word: "Raised", buttons: ["superscript"] },
    { word: "colored", buttons: [], color: kRed },
    {
        word: "everything",
        buttons: ["bold", "italic", "underline", "superscript"],
        color: kRed,
    },
    { word: "finish", buttons: [] },
];
const kText = "Start bold italic underlined. Raised colored everything finish.";
const kSentences = [
    "Start bold italic underlined.",
    "Raised colored everything finish.",
];

/** The formatting each word of kWords should have. */
const kExpectedFormatting: Record<string, IWordFormatting> = Object.fromEntries(
    kWords.map(({ word, buttons, color }) => [
        word,
        {
            bold: buttons.includes("bold"),
            italic: buttons.includes("italic"),
            underline: buttons.includes("underline"),
            superscript: buttons.includes("superscript"),
            color: color ?? "",
        },
    ]),
);

/** A stretch of a box's text that is formatted alike throughout. */
interface IRun {
    text: string;
    formatting: IWordFormatting;
}

/**
 * The runs kText should be drawn in: each formatted word a run of its own, and everything between
 * them, spaces and full stops included, plain.
 */
function expectedRuns(): IRun[] {
    const runs: IRun[] = [];
    let rest = kText;
    for (const { word } of kWords) {
        const formatting = kExpectedFormatting[word];
        const start = rest.indexOf(word);
        const before = rest.slice(0, start);
        rest = rest.slice(start + word.length);
        const plain = Object.values(formatting).every((v) => !v);
        const last = runs.at(-1);
        const lastIsPlain =
            !!last && Object.values(last.formatting).every((v) => !v);
        // Plain words join the plain text around them, as they do in the box.
        if (plain) {
            if (lastIsPlain) last!.text += before + word;
            else
                runs.push({
                    text: before + word,
                    formatting: { ...formatting },
                });
            continue;
        }
        if (before) {
            if (lastIsPlain) last!.text += before;
            else
                runs.push({
                    text: before,
                    formatting: { ...kExpectedFormatting["Start"] },
                });
        }
        runs.push({ text: word, formatting: { ...formatting } });
    }
    if (rest) {
        const last = runs.at(-1);
        if (last && Object.values(last.formatting).every((v) => !v))
            last.text += rest;
        else
            runs.push({
                text: rest,
                formatting: { ...kExpectedFormatting["Start"] },
            });
    }
    return runs;
}

/** The English text box of the book's one content page, as the saved book file has it. */
interface IBoxOnDisk {
    /** The box's text. */
    text: string;
    /**
     * The box's text cut into runs formatted alike, every letter, space and stop of it, so that
     * formatting that spread past the word it was given shows as a different cut.
     */
    runs: IRun[];
    /** The id and text of each sentence the Talking Book tool marked for recording. */
    sentences: { id: string; text: string }[];
}

/**
 * The English text box of the one content page of the book in `bookFolder`, as its saved file
 * has it. `page` is used only as a DOM parser.
 */
async function readSavedBox(
    page: Page,
    bookFolder: string,
): Promise<IBoxOnDisk> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    const box = await page.evaluate(
        ({ source }) => {
            const dom = new DOMParser().parseFromString(source, "text/html");
            const boxes = Array.from(
                dom.querySelectorAll<HTMLElement>(
                    ".bloom-page:not(.bloom-frontMatter):not(.bloom-backMatter) " +
                        '.bloom-translationGroup .bloom-editable[lang="en"]',
                ),
            );
            if (boxes.length !== 1) return `${boxes.length} English text boxes`;
            const element = boxes[0];
            const normalize = (text: string) =>
                text
                    .replace(/[​﻿]/g, "")
                    .replace(/[ \t\r\n\f]+/g, " ")
                    .trim();
            // The color a style attribute gives, as "#rrggbb". The element's style parses it, so
            // "#FF1616", "#ff1616" and "rgb(255, 22, 22)" all come out alike.
            const toHex = (cssColor: string) => {
                const match = cssColor.match(/^rgb\((\d+), (\d+), (\d+)\)$/);
                if (!match) return cssColor;
                return (
                    "#" +
                    match
                        .slice(1)
                        .map((n) => Number(n).toString(16).padStart(2, "0"))
                        .join("")
                );
            };
            // Every character of the box with the formatting of the text node that holds it, with
            // the whitespace of the file folded as the page folds it.
            const runs: {
                text: string;
                formatting: {
                    bold: boolean;
                    italic: boolean;
                    underline: boolean;
                    superscript: boolean;
                    color: string;
                };
            }[] = [];
            const walker = dom.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            for (let n = walker.nextNode(); n; n = walker.nextNode()) {
                const found = {
                    bold: false,
                    italic: false,
                    underline: false,
                    superscript: false,
                    color: "",
                };
                for (
                    let e = n.parentElement;
                    e && e !== element;
                    e = e.parentElement
                ) {
                    const tag = e.tagName.toLowerCase();
                    if (tag === "strong" || tag === "b") found.bold = true;
                    if (tag === "em" || tag === "i") found.italic = true;
                    if (tag === "u") found.underline = true;
                    if (tag === "sup") found.superscript = true;
                    // The innermost color is the one that shows.
                    if (!found.color && e.style.color)
                        found.color = toHex(e.style.color);
                }
                for (const raw of (n.textContent ?? "").replace(/[​﻿]/g, "")) {
                    const char = /[ \t\r\n\f]/.test(raw) ? " " : raw;
                    const last = runs.at(-1);
                    const soFar = runs.map((r) => r.text).join("");
                    if (char === " " && (soFar === "" || soFar.endsWith(" ")))
                        continue;
                    if (
                        last &&
                        JSON.stringify(last.formatting) ===
                            JSON.stringify(found)
                    )
                        last.text += char;
                    else runs.push({ text: char, formatting: { ...found } });
                }
            }
            // A trailing space the file's layout left.
            const last = runs.at(-1);
            if (last) {
                last.text = last.text.replace(/ $/, "");
                if (!last.text) runs.pop();
            }
            return {
                text: normalize(element.textContent ?? ""),
                runs,
                sentences: Array.from(
                    element.querySelectorAll(".audio-sentence[id]"),
                ).map((s) => ({
                    id: s.id,
                    text: normalize(s.textContent ?? ""),
                })),
            };
        },
        { source: html },
    );
    if (typeof box === "string")
        throw new Error(
            `The book in ${bookFolder} should have one content page with one English text box; it has ${box}.`,
        );
    return box as IBoxOnDisk;
}

/** A SHA-1 of the file's bytes. */
function fileHash(path: string): string {
    return crypto
        .createHash("sha1")
        .update(fs.readFileSync(path))
        .digest("hex");
}

test("text formatted with the selection toolbar keeps its formatting through a spreadsheet [Test Case ID 634]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;

    const original = await step(
        "Make a book whose text has every kind of selection formatting, and record it",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text", 1);
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await typeInGroup(page, kGroup, "en", kText);
            const box = await clickInGroup(page, kGroup, "en");
            for (const { word, buttons, color } of kWords) {
                if (buttons.length === 0 && !color) continue;
                await selectTextIn(page, box, word);
                for (const button of buttons)
                    expect(
                        await toggleSelectionFormatting(page, button),
                        `${button} should be on for "${word}"`,
                    ).toBe(true);
                if (color) await chooseSelectionTextColor(page, color);
            }
            await waitForSnapshotStreamIdle(page);

            await openToolboxWithTalkingBook(page);
            await setRecordingMode(page, "By Sentence");
            const ids = await addNarration(page, bookFolder, "en");
            // A recording of its own for the second sentence, so that recordings swapped or
            // shared between the sentences show.
            writeSecondSampleNarration(narrationFilePath(bookFolder, ids[1]));
            // Leaving the Edit tab saves the book.
            await switchTab(page, "collection");
            return bookFolder;
        },
    );
    if (!original) throw new Error("The book was not made.");
    const originalBox = await readSavedBox(page, original);
    /**
     * A hash of each sentence's recording, in the order of kSentences. A missing recording is left
     * for the check below, which names its sentence.
     */
    const originalRecordings = originalBox.sentences.map((sentence) => {
        const recording = narrationFilePath(original, sentence.id);
        return fs.existsSync(recording)
            ? fileHash(recording)
            : `no recording of "${sentence.text}"`;
    });

    await step(
        "Check the saved book has the formatting and the recordings",
        () => {
            // Without this the round trip below could pass on text that was never formatted.
            expect(originalBox.text).toBe(kText);
            expect(
                originalBox.runs,
                "each word of the saved box should have the formatting it was given, and nothing else any",
            ).toEqual(expectedRuns());
            expect(
                originalBox.sentences.map((s) => s.text),
                "the saved box should be marked as two recorded sentences",
            ).toEqual(kSentences);
            for (const sentence of originalBox.sentences)
                expect(
                    fs.existsSync(narrationFilePath(original, sentence.id)),
                    `"${sentence.text}" should have its recording in the book's audio folder`,
                ).toBe(true);
            expect(
                new Set(originalRecordings).size,
                "the two sentences should have different recordings",
            ).toBe(2);
        },
    );

    const exportFolder = testInfo.outputPath("spreadsheets");
    fs.mkdirSync(exportFolder, { recursive: true });
    const spreadsheet = await step("Export it to a spreadsheet", async () => {
        await switchTab(page, "collection");
        await selectBook(page, original);
        return exportSelectedBookToSpreadsheet(page, exportFolder);
    });
    if (!spreadsheet) throw new Error("The book was not exported.");

    const newBookId = await step("Make a new blank book", async () => {
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
            [...progress.errors, ...progress.warnings],
            `The import said:\n${progress.messages.join("\n")}`,
        ).toEqual([]);
        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem during the round trip",
        ).toEqual([]);
    });

    await step(
        "Check the new book's text has the same formatting and recordings",
        async () => {
            const imported = findBookFolderById(
                bloomApp.collectionDir,
                newBookId,
            );
            const importedBox = await readSavedBox(page, imported);
            expect(importedBox.text).toBe(kText);
            expect(
                importedBox.runs,
                "each word of the new book should have the formatting it had in the original, and nothing else any",
            ).toEqual(originalBox.runs);
            expect(
                importedBox.sentences.map((s) => s.text),
                "the new book should have the same recorded sentences",
            ).toEqual(kSentences);
            importedBox.sentences.forEach((sentence, index) => {
                const recording = narrationFilePath(imported, sentence.id);
                expect(
                    fs.existsSync(recording),
                    `"${sentence.text}" should have its recording in the new book, ${recording}`,
                ).toBe(true);
                expect(
                    fileHash(recording),
                    `"${sentence.text}" should play the recording it had in the original`,
                ).toBe(originalRecordings[index]);
            });
        },
    );

    await step(
        "Open the new book's page and check each word shows its formatting",
        async () => {
            await editBook(
                page,
                findBookFolderById(bloomApp.collectionDir, newBookId),
            );
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            const box = await clickInGroup(page, kGroup, "en");
            await expect(box).toHaveText(kText, {
                timeout: 30000,
                useInnerText: true,
            });
            const plainColor = (await getTextRunFormatting(box, "Start")).color;
            for (const { word } of kWords) {
                const expected = kExpectedFormatting[word];
                expect(
                    await getTextRunFormatting(box, word),
                    `"${word}" as the new book's page shows it`,
                ).toEqual({
                    bold: expected.bold,
                    italic: expected.italic,
                    underline: expected.underline,
                    color: expected.color || plainColor,
                });
                expect(
                    await box.evaluate((boxElement, wanted) => {
                        const walker = document.createTreeWalker(
                            boxElement,
                            NodeFilter.SHOW_TEXT,
                        );
                        for (
                            let n = walker.nextNode();
                            n;
                            n = walker.nextNode()
                        )
                            if (n.textContent?.includes(wanted)) {
                                // Raised text is drawn by an element with vertical-align: super,
                                // which is not inherited: look at each element out to the box.
                                for (
                                    let e = n.parentElement;
                                    e && boxElement.contains(e);
                                    e = e.parentElement
                                )
                                    if (
                                        getComputedStyle(e).verticalAlign ===
                                        "super"
                                    )
                                        return true;
                                return false;
                            }
                        throw new Error(`The box does not hold "${wanted}".`);
                    }, word),
                    `"${word}" should ${expected.superscript ? "" : "not "}show raised`,
                ).toBe(expected.superscript);
            }
            expect(plainColor, "plain text should not be red").not.toBe(kRed);
        },
    );
});
