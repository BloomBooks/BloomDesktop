// A text box recorded as a whole and split into segments with segment markers ("|") keeps its
// recording aligned with those segments when its book goes through a spreadsheet: exported with
// More > Export to Spreadsheet..., then imported with More > Import Content from Spreadsheet...
// into a new book, which ends up with the same segments, each ending at the same moment of the
// same recording.
//
// Automates the manual test "Spreadsheet Keeps Audio Alignment" (Test Case ID 631).
//
// A person records the box with a microphone; the test puts a recording on it with the Talking
// Book tool's Import Recording, the one way Bloom takes a whole-text-box recording without one.
// The case's "aligned" is a person listening to the new book's recording while its segments
// light up. What decides that is data: which segment the reader highlights during which part of
// the recording, and the test compares exactly that (each segment's text and end time, and the
// recording's bytes) between the two books. The times are the ones Adjust Timings... proposes
// and OK saves, which is how a person aligns a recording with the segments they made.
//
// The text is typed, markers and all, before the Talking Book tool opens, rather than with the
// tool open and its Insert Segment Marker button, as the case has it: typing with the tool open
// can stall the page's updates to Bloom (BL-17186). Opening the tool turns each typed "|" into a
// segment marker as the button does (talking-book-segment-marker.spec.ts checks that).
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
    editablePageFrame,
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
    exportSelectedBookToSpreadsheet,
    importSpreadsheetIntoSelectedBook,
} from "../helpers/spreadsheet";
import {
    acceptProposedTimings,
    importNarration,
    narrationFilePath,
    openToolboxWithTalkingBook,
} from "../helpers/talkingBook";
import { hideToolbox } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "spreadsheet-segmented-audio",
        languages: ["en"],
        // Recording a whole text box, Import Recording, and the spreadsheet commands all need a
        // subscription: Pro for the first two, Local Community for the last.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

const kGroup = ".bloom-translationGroup";

// The case's two sentences, each in a paragraph of its own, with a segment marker before "good",
// "Satyr", "Dionysus" and "to".
const kFirstParagraph = "The Greek king Midas did a |good deed for a |Satyr.";
const kSecondParagraph =
    "This prompted |Dionysus, the god of wine, |to grant him a wish.";

/** The segments the markers and the sentence ends cut the text into, each marker shown as "|". */
const kSegments = [
    "The Greek king Midas did a |",
    "good deed for a |",
    "Satyr.",
    "This prompted |",
    "Dionysus, the god of wine, |",
    "to grant him a wish.",
];

/** The English text box of the book's one content page, as the saved book file has it. */
interface IBoxOnDisk {
    /** The box's text, with each segment marker shown as "|". */
    text: string;
    /** How many segment markers the box holds. */
    markers: number;
    /** "TextBox" when the box is recorded as a whole. */
    recordingMode: string;
    /** The box's id, which names its recording in audio/. */
    id: string;
    /** Whether Bloom shows the box as split into timed segments (bloom-postAudioSplit). */
    split: boolean;
    /** The text of each segment the reader highlights in turn, markers shown as "|". */
    segments: string[];
    /**
     * When each segment ends in the recording, in seconds: data-audiorecordingendtimes as written,
     * or null when the box has none. Playback reads it field by field (audioRecording.ts
     * setupTimings), so it is kept as it is, an empty field and all.
     */
    endTimes: string | null;
}

/**
 * The fields of an end-times attribute, as playback splits it: an empty field (a doubled space)
 * stays an empty field, which playback would take for 0.
 */
function endTimeFields(endTimes: string | null): string[] {
    return endTimes === null ? [] : endTimes.split(" ");
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
    const box = await page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        const boxes = Array.from(
            dom.querySelectorAll<HTMLElement>(
                ".bloom-page:not(.bloom-frontMatter):not(.bloom-backMatter) " +
                    '.bloom-translationGroup .bloom-editable[lang="en"]',
            ),
        );
        if (boxes.length !== 1) return `${boxes.length} English text boxes`;
        // A segment marker is an empty span holding a zero-width space; show it as the "|" a
        // person sees while the Talking Book tool is open.
        const shown = (element: Element) => {
            const copy = element.cloneNode(true) as Element;
            copy.querySelectorAll(".bloom-audio-split-marker").forEach(
                (marker) => marker.replaceWith("|"),
            );
            return (copy.textContent ?? "")
                .replace(/​/g, "")
                .replace(/[ \t\r\n\f]+/g, " ")
                .trim();
        };
        const element = boxes[0];
        return {
            text: Array.from(element.querySelectorAll("p"))
                .map(shown)
                .join("\n"),
            markers: element.querySelectorAll(".bloom-audio-split-marker")
                .length,
            recordingMode:
                element.getAttribute("data-audiorecordingmode") ?? "",
            id: element.id,
            split: element.classList.contains("bloom-postAudioSplit"),
            segments: Array.from(
                element.querySelectorAll(".bloom-highlightSegment"),
            ).map(shown),
            endTimes: element.getAttribute("data-audiorecordingendtimes"),
        };
    }, html);
    if (typeof box === "string")
        throw new Error(
            `The book in ${bookFolder} should have one content page with one English text box; it has ${box}.`,
        );
    return box;
}

/** A SHA-1 of the file's bytes. */
function fileHash(path: string): string {
    return crypto
        .createHash("sha1")
        .update(fs.readFileSync(path))
        .digest("hex");
}

test("a recording aligned to segment markers stays aligned through a spreadsheet [Test Case ID 631]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;

    const original = await step(
        "Make a book whose text box is recorded as a whole and aligned to its segments",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text", 1);
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await typeInGroup(page, kGroup, "en", kFirstParagraph, 0, false);
            await page.keyboard.press("Enter");
            await page.keyboard.insertText(kSecondParagraph);
            await expect(
                editablePageFrame(page)
                    .locator(`${kGroup} .bloom-editable[lang="en"]`)
                    .first(),
            ).toHaveText(`${kFirstParagraph}\n${kSecondParagraph}`, {
                timeout: 15000,
                useInnerText: true,
            });
            await waitForSnapshotStreamIdle(page);

            await openToolboxWithTalkingBook(page);
            await importNarration(page, bookFolder, kGroup, "en");
            await acceptProposedTimings(page);
            // Leaving the Edit tab saves the book.
            await switchTab(page, "collection");
            return bookFolder;
        },
    );
    if (!original) throw new Error("The book was not made.");
    const originalBox = await readSavedBox(page, original);

    await step(
        "Check the saved book has the recording aligned to the segments",
        () => {
            // Without this the round trip below could pass on a box that was never split.
            expect(
                {
                    text: originalBox.text,
                    markers: originalBox.markers,
                    recordingMode: originalBox.recordingMode,
                    split: originalBox.split,
                    segments: originalBox.segments,
                },
                "the saved box should be recorded as a whole and split at its markers and sentence ends",
            ).toEqual({
                text: `${kFirstParagraph}\n${kSecondParagraph}`,
                markers: 4,
                recordingMode: "TextBox",
                split: true,
                segments: kSegments,
            });
            const fields = endTimeFields(originalBox.endTimes);
            expect(
                fields.length,
                `each segment should have an end time: "${originalBox.endTimes}"`,
            ).toBe(kSegments.length);
            fields.forEach((field, index) => {
                expect(
                    field !== "" && Number.isFinite(Number(field)),
                    `end time ${index} should be a number: "${originalBox.endTimes}"`,
                ).toBe(true);
                expect(
                    Number(field),
                    `segment ${index} should end after the one before it: "${originalBox.endTimes}"`,
                ).toBeGreaterThan(index === 0 ? 0 : Number(fields[index - 1]));
            });
            expect(
                fs.existsSync(narrationFilePath(original, originalBox.id)),
                "the box's recording should be in the book's audio folder",
            ).toBe(true);
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
        "Check the new book's recording is aligned to the same segments",
        async () => {
            const imported = findBookFolderById(
                bloomApp.collectionDir,
                newBookId,
            );
            const importedBox = await readSavedBox(page, imported);
            // The import keeps every end time the spreadsheet gives but the last, which it sets to
            // the length it measures of the recording (SpreadsheetImporter.cs, "Set the end
            // times"). That can differ a little from the time Adjust Timings saved, so the last is
            // compared to within a quarter of a second, and the rest exactly, as written.
            const withoutLastEnd = (box: IBoxOnDisk) => ({
                text: box.text,
                markers: box.markers,
                recordingMode: box.recordingMode,
                split: box.split,
                segments: box.segments,
                endTimeCount: endTimeFields(box.endTimes).length,
                endTimesButLast: endTimeFields(box.endTimes).slice(0, -1),
            });
            expect(withoutLastEnd(importedBox)).toEqual(
                withoutLastEnd(originalBox),
            );
            const importedLast = Number(
                endTimeFields(importedBox.endTimes).at(-1),
            );
            const originalLast = Number(
                endTimeFields(originalBox.endTimes).at(-1),
            );
            expect(
                Math.abs(importedLast - originalLast),
                `the last segment should end where the original's does, ${originalLast} s, not ${importedLast} s`,
            ).toBeLessThan(0.25);
            // The segments must play the same recording, not just one of the same name.
            const recording = narrationFilePath(imported, importedBox.id);
            expect(
                fs.existsSync(recording),
                `the new book should have its box's recording, ${recording}`,
            ).toBe(true);
            expect(
                fileHash(recording),
                "the new book's recording should be the original's",
            ).toBe(fileHash(narrationFilePath(original, originalBox.id)));
        },
    );

    await step(
        "Open the new book's page with the Talking Book tool, then close the tool",
        async () => {
            const imported = findBookFolderById(
                bloomApp.collectionDir,
                newBookId,
            );
            const savedEndTimes = (await readSavedBox(page, imported)).endTimes;
            await editBook(page, imported);
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await openToolboxWithTalkingBook(page);
            const box = await clickInGroup(page, kGroup, "en");
            await expect(
                box,
                'With the Talking Book tool open, the new book should show each segment marker as "|".',
            ).toHaveText(`${kFirstParagraph}\n${kSecondParagraph}`, {
                timeout: 60000,
                useInnerText: true,
            });
            // The tool must take the box as it is, recording and timings and all, rather than
            // treat the imported text as changed since it was recorded.
            await expect(
                box,
                "With the Talking Book tool open, the new book's box should still be split into timed segments.",
            ).toHaveClass(/\bbloom-postAudioSplit\b/, { timeout: 30000 });
            // Playback times the highlighting from the box's live end times, so they must be the
            // ones the import saved.
            expect(
                await box.getAttribute("data-audiorecordingendtimes"),
                "the open box should keep the end times the import saved",
            ).toBe(savedEndTimes);
            expect(
                await box.locator(".bloom-highlightSegment").count(),
                "the open box should still have one highlight segment per segment",
            ).toBe(kSegments.length);

            await hideToolbox(page);
            await expect(
                box,
                'With the Talking Book tool closed, the "|" segment markers should no longer show.',
            ).toHaveText(
                `${kFirstParagraph}\n${kSecondParagraph}`.replace(/\|/g, ""),
                { timeout: 30000, useInnerText: true },
            );
        },
    );
});
