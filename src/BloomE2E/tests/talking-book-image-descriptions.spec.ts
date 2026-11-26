// Recording image descriptions with the Talking Book tool: on content pages, by sentence and by
// whole text box, and on the front cover, alongside the narration of the pages' own text.
//
// Image descriptions are hidden unless a tool shows them. The Talking Book tool shows them by
// itself whenever a page has a description with text in it, so that they can be recorded
// (talkingBookTool.tsx showImageDescriptionsIfAny, BL-8515). That, and the tool marking each
// description for recording in the mode chosen for it, is what this file checks.
//
// Recording needs a microphone, which a test has not got. So "recording" a sentence here is
// addNarration's way: put the mp3 where Bloom's recording of it would go, under the id that the
// tool gave the sentence. The whole-text-box description goes in through the tool's real Import
// Recording button (importNarration), which is the one way Bloom itself puts a recording on a
// whole text box without a microphone.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { bookHtmlPath } from "../helpers/bookHtml";
import { setBranding } from "../helpers/collectionSettings";
import {
    importNarration,
    narrationFilePath,
    sampleNarrationFile,
    setRecordingMode,
} from "../helpers/talkingBook";
import {
    enableToolForBook,
    expandTool,
    expandToolByHeader,
} from "../helpers/toolbox";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: {
        name: "talking-book-image-descriptions",
        languages: ["en"],
    },
});

const kDescription = ".bloom-imageDescription";
const kPageText = ".bloom-translationGroup:not(.bloom-imageDescription)";

// Each description has several sentences, so that recording it by sentence is distinguishable
// from recording it as one whole text box.
const kCoverDescription = [
    "A big tree stands on a hill.",
    "Its leaves are green.",
];
const kBySentenceDescription = [
    "A dog runs in the grass.",
    "It is chasing a ball.",
    "The sun is shining.",
];
const kWholeBoxDescription = [
    "A cat sleeps by the fire.",
    "It is warm and happy.",
];
const kPageOneText = ["The dog likes to play.", "It plays all day."];
const kPageTwoText = ["The cat likes to sleep.", "It sleeps all night."];

test("records image descriptions by sentence, by whole text box and on the cover, with the page text [Test Case ID 569]", async ({
    page,
}) => {
    test.setTimeout(420000);

    // Whole-text-box recording, and so Import Recording, is a Pro-tier feature; see
    // import-recording.spec.ts for why this is the way a test gets it.
    await setBranding(page, "Sample-Pro");

    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Basic Text & Image", 2);
    await addPage(page, "Just Text", 1);
    await setContentLanguages(page, ["en"]);
    await enableToolForBook(page, bookFolder, "imageDescription");

    const cover = (await getPages(page))[0];
    const [bySentencePage, wholeBoxPage] = await getContentPages(page);
    expect(cover.isContentPage, "The first page is not the cover.").toBe(false);

    // Write the descriptions with the Image Description tool, as a person does, and the pages'
    // own text.
    await goToPage(page, cover.id);
    await expandTool(page, "imageDescription", ".imgDescLabelBlock");
    await typeInGroup(page, kDescription, "en", kCoverDescription.join(" "));
    await goToPage(page, bySentencePage.id);
    await typeInGroup(
        page,
        kDescription,
        "en",
        kBySentenceDescription.join(" "),
    );
    await typeInGroup(page, kPageText, "en", kPageOneText.join(" "));
    await goToPage(page, wholeBoxPage.id);
    await typeInGroup(page, kDescription, "en", kWholeBoxDescription.join(" "));
    await typeInGroup(page, kPageText, "en", kPageTwoText.join(" "));

    // From here on the Image Description tool is shut, so whatever shows the descriptions is the
    // Talking Book tool.
    await expandToolByHeader(page, "talkingBook");

    // A description recorded by sentence: each of its sentences is a recording of its own.
    await goToPage(page, bySentencePage.id);
    await expectDescriptionsShown(page);
    await clickInGroup(page, kDescription, "en");
    await setRecordingMode(page, "By Sentence");
    const bySentenceIds = await expectSentencesMarked(
        page,
        kDescription,
        kBySentenceDescription,
    );
    const pageOneTextIds = await expectSentencesMarked(
        page,
        kPageText,
        kPageOneText,
    );
    recordSentences(bookFolder, [...bySentenceIds, ...pageOneTextIds]);

    // A description recorded as a whole text box: one recording for all of it.
    await goToPage(page, wholeBoxPage.id);
    await expectDescriptionsShown(page);
    await expectSentencesMarked(page, kDescription, kWholeBoxDescription);
    await importNarration(page, bookFolder, kDescription, "en");
    const wholeBox = editablePageFrame(page)
        .locator(`${kDescription} .bloom-editable[lang="en"]`)
        .first();
    await expect(
        wholeBox,
        "After Import Recording in whole-text-box mode, the description is not one recording.",
    ).toHaveClass(/\baudio-sentence\b/, { timeout: 15000 });
    await expect(
        wholeBox.locator(".audio-sentence"),
        "The description recorded as a whole text box still holds sentence recordings.",
    ).toHaveCount(0, { timeout: 15000 });
    const wholeBoxId = (await wholeBox.getAttribute("id")) ?? "";
    expect(
        fs.existsSync(narrationFilePath(bookFolder, wholeBoxId)),
        `Import Recording made no ${narrationFilePath(bookFolder, wholeBoxId)} for the description.`,
    ).toBe(true);
    const pageTwoTextIds = await expectSentencesMarked(
        page,
        kPageText,
        kPageTwoText,
    );
    recordSentences(bookFolder, pageTwoTextIds);

    // The front cover's picture has a description too, and it is recorded the same way. The tool
    // gives a box that has no recording the mode it was last in, whole text box after the page
    // above, so choose By Sentence again.
    await goToPage(page, cover.id);
    await expectDescriptionsShown(page);
    await clickInGroup(page, kDescription, "en");
    await setRecordingMode(page, "By Sentence");
    const coverIds = await expectSentencesMarked(
        page,
        kDescription,
        kCoverDescription,
    );
    recordSentences(bookFolder, coverIds);

    // Leaving the cover saves it. What the saved book says is what Bloom Player, ePUB and the
    // rest go by when they play the recordings.
    await goToPage(page, bySentencePage.id);
    const saved = await readSavedRecordings(page, bookFolder);
    expect(saved[cover.id], "The saved cover's description").toEqual({
        mode: "Sentence",
        descriptionIds: coverIds,
        textIds: [],
    });
    expect(
        saved[bySentencePage.id],
        "The saved page whose description was recorded by sentence",
    ).toEqual({
        mode: "Sentence",
        descriptionIds: bySentenceIds,
        textIds: pageOneTextIds,
    });
    expect(
        saved[wholeBoxPage.id],
        "The saved page whose description was recorded as a whole text box",
    ).toEqual({
        mode: "TextBox",
        descriptionIds: [wholeBoxId],
        textIds: pageTwoTextIds,
    });
});

/** Wait until the page being edited is showing its image descriptions. */
async function expectDescriptionsShown(page: Page): Promise<void> {
    await expect(
        editablePageFrame(page).locator("body"),
        "The Talking Book tool did not show the page's image description, so it cannot be recorded.",
    ).toHaveClass(/\bbloom-showImageDescriptions\b/, { timeout: 30000 });
    await expect(
        editablePageFrame(page)
            .locator(`${kDescription} .bloom-editable[lang="en"]`)
            .first(),
    ).toBeVisible({ timeout: 30000 });
}

/**
 * Wait until the Talking Book tool has marked each of `sentences`, in order, as a sentence to
 * record in the "en" box of the first group `groupSelector` picks, and return their ids.
 */
async function expectSentencesMarked(
    page: Page,
    groupSelector: string,
    sentences: string[],
): Promise<string[]> {
    const marked = editablePageFrame(page)
        .locator(groupSelector)
        .first()
        .locator('.bloom-editable[lang="en"] .audio-sentence[id]');
    await expect
        .poll(
            async () =>
                (await marked.allTextContents()).map((text) => text.trim()),
            {
                timeout: 30000,
                message: `The Talking Book tool did not mark each sentence of "${groupSelector}" for recording.`,
            },
        )
        .toEqual(sentences);
    return marked.evaluateAll((elements) => elements.map((e) => e.id));
}

/** Put the recording of each of these sentences where Bloom's own recording would go. */
function recordSentences(bookFolder: string, ids: string[]): void {
    fs.mkdirSync(Path.join(bookFolder, "audio"), { recursive: true });
    for (const id of ids)
        fs.copyFileSync(
            Path.resolve(sampleNarrationFile),
            narrationFilePath(bookFolder, id),
        );
}

interface ISavedRecordings {
    /** The recording mode the "en" description box was saved with. */
    mode: string;
    /** The ids of the recordings in the description, each of which has its audio file. */
    descriptionIds: string[];
    /** The ids of the recordings in the page's own text, each of which has its audio file. */
    textIds: string[];
}

/** What the saved book holds for recordings, page by page, read from the book file on disk. */
async function readSavedRecordings(
    page: Page,
    bookFolder: string,
): Promise<Record<string, ISavedRecordings>> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    const saved = await page.evaluate(
        ({ source, description, pageText }) => {
            const dom = new DOMParser().parseFromString(source, "text/html");
            const result: Record<
                string,
                { mode: string; descriptionIds: string[]; textIds: string[] }
            > = {};
            const recordingIds = (group: Element | null) =>
                group
                    ? Array.from(
                          group.querySelectorAll(
                              '.bloom-editable[lang="en"].audio-sentence[id], .bloom-editable[lang="en"] .audio-sentence[id]',
                          ),
                      ).map((e) => e.id)
                    : [];
            for (const pageDiv of Array.from(
                dom.querySelectorAll(".bloom-page[id]"),
            )) {
                const descriptionGroup = pageDiv.querySelector(description);
                if (!descriptionGroup) continue;
                result[pageDiv.id] = {
                    mode:
                        descriptionGroup
                            .querySelector('.bloom-editable[lang="en"]')
                            ?.getAttribute("data-audiorecordingmode") ?? "",
                    descriptionIds: recordingIds(descriptionGroup),
                    textIds: recordingIds(pageDiv.querySelector(pageText)),
                };
            }
            return result;
        },
        { source: html, description: kDescription, pageText: kPageText },
    );
    for (const recordings of Object.values(saved))
        for (const id of [...recordings.descriptionIds, ...recordings.textIds])
            expect(
                fs.existsSync(narrationFilePath(bookFolder, id)),
                `The saved book has a recording ${id} with no audio file.`,
            ).toBe(true);
    return saved;
}
