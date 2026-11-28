// The Talking Book tool's playback order, with an image description among the recordings: a page
// plays its recordings in document order until someone sets an order, and once someone moves a
// group with the playback order buttons, the published book plays the page in that order.
//
// Automates the manual test "Change Playback Ordering" (Test Case ID 253), with the steps of its
// older form (bloom-test-cases TC290): a "Basic Text & Image" page whose text and image description
// are both recorded; in the Talking Book tool's Advanced section "Show image descriptions" is
// already on, "Show playback order buttons" numbers the description 1 and the text 2, and the +
// beside the description's number swaps them; the ePUB preview's Play then reads the text first and
// the description next.
//
// Recording needs a microphone, which a test has not got: addNarration puts a recording under each
// id the Talking Book tool gave a sentence, where Bloom's own recording would go. So the case's
// "Check" of each recording is not repeated here. The two recordings sound different (the
// description's starts silent), so the test also checks that each sentence plays its own. The ePUB is read from the ePUB screen's preview,
// which shows the very files that Save... zips.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    getEpubPreviewAudioLoudness,
    getEpubPreviewPageReadAloud,
    idInEpub,
    openEpubPublishScreen,
    playEpubPreviewReadAlong,
    showEpubPreview,
    showEpubPreviewPage,
} from "../helpers/epubPublish";
import {
    kImageDescriptionGroup,
    openImageDescriptionTool,
} from "../helpers/imageDescription";
import { chooseImageFile } from "../helpers/images";
import {
    clickPlaybackOrderButton,
    getGroupsInDocumentOrder,
    getPlaybackOrderNumber,
    getPlaybackOrderSetOnPage,
    isTalkingBookSwitchOn,
    showPlaybackOrderButtons,
} from "../helpers/playbackOrder";
import {
    addNarration,
    narrationFilePath,
    sampleNarrationFile,
    silentMp3Frames,
    waitForSentencesMarked,
} from "../helpers/talkingBook";
import { enableToolForBook, expandToolByHeader } from "../helpers/toolbox";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "talking-book-playback-order",
        languages: ["en"],
        // The image description tool and publishing an ePUB need a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const L1 = "en";
const PICTURE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

const PAGE_TEXT = "This is a car.";
const DESCRIPTION = "This is a car image.";
const TEXT_GROUP = `.bloom-translationGroup:not(${kImageDescriptionGroup})`;
/** Seconds of silence at the start of the description's recording, which the text's has not. */
const DESCRIPTION_SILENCE = 1.2;

test("changing the playback order, with an image description, changes the order the page plays in [Test Case ID 253]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);

    const book = await step(
        "Make a book with a Basic Text & Image page whose picture has a description, and record the text and the description",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Basic Text & Image");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await chooseImageFile(page, PICTURE);
            await typeInGroup(page, TEXT_GROUP, L1, PAGE_TEXT);
            await enableToolForBook(page, bookFolder, "imageDescription");
            await openImageDescriptionTool(page);
            await typeInGroup(page, kImageDescriptionGroup, L1, DESCRIPTION);

            await expandToolByHeader(page, "talkingBook");
            const textIds = await waitForSentencesMarked(page, TEXT_GROUP, L1, [
                PAGE_TEXT,
            ]);
            const descriptionIds = await waitForSentencesMarked(
                page,
                kImageDescriptionGroup,
                L1,
                [DESCRIPTION],
            );
            const narrated = await addNarration(page, bookFolder, L1);
            expect(
                [...narrated].sort(),
                "test setup: the page's recordings should be the text's and the description's",
            ).toEqual([...textIds, ...descriptionIds].sort());
            // The two recordings must sound different, or audio played under the wrong text would
            // go unnoticed: the text's sounds from its start, the description's starts silent.
            expect(
                [textIds.length, descriptionIds.length],
                "test setup: the text and the description should each be one sentence",
            ).toEqual([1, 1]);
            fs.writeFileSync(
                narrationFilePath(bookFolder, descriptionIds[0]),
                Buffer.concat([
                    silentMp3Frames(DESCRIPTION_SILENCE),
                    fs.readFileSync(sampleNarrationFile),
                ]),
            );
            return {
                pageId: contentPage.id,
                text: { id: textIds[0], recording: "text" as const },
                description: {
                    id: descriptionIds[0],
                    recording: "description" as const,
                },
            };
        },
    );
    if (!book) throw new Error("The book was not made.");

    await step(
        'Check that the Talking Book tool\'s Advanced section already has "Show image descriptions" on',
        async () => {
            expect(
                await isTalkingBookSwitchOn(page, "Show Image Descriptions"),
                'with the Image Description tool used on the page, "Show image descriptions" should already be on',
            ).toBe(true);
        },
    );

    await step(
        "Check that, with no playback order set, the ePUB reads the page in document order: the description, then the text",
        async () => {
            expect(
                await getPlaybackOrderSetOnPage(page),
                "test setup: no group on the page should have a playback order yet",
            ).toEqual([]);
            expect(
                await getGroupsInDocumentOrder(page, [
                    TEXT_GROUP,
                    kImageDescriptionGroup,
                ]),
                "test setup: on a Basic Text & Image page the picture, with its description, comes before the text",
            ).toEqual([kImageDescriptionGroup, TEXT_GROUP]);
            await expectEpubToRead(
                page,
                book.pageId,
                [book.description, book.text],
                "with no playback order set, the description then the text",
            );
        },
    );

    await step(
        'Turn on "Show playback order buttons", and check that the description is numbered 1 and the text 2',
        async () => {
            await switchTab(page, "edit");
            await goToPage(page, book.pageId);
            await expandToolByHeader(page, "talkingBook");
            await showPlaybackOrderButtons(page, 2);
            expect(
                await getPlaybackOrderNumber(page, kImageDescriptionGroup),
                "the image description should be first in the playback order",
            ).toBe(1);
            expect(
                await getPlaybackOrderNumber(page, TEXT_GROUP),
                "the text should be second in the playback order",
            ).toBe(2);
        },
    );

    await step(
        "Click the + beside the description's number, and check that the description is now 2 and the text 1",
        async () => {
            await clickPlaybackOrderButton(
                page,
                kImageDescriptionGroup,
                "later",
            );
            expect(
                await getPlaybackOrderNumber(page, kImageDescriptionGroup),
                "after its + was clicked, the image description should be second in the playback order",
            ).toBe(2);
            expect(
                await getPlaybackOrderNumber(page, TEXT_GROUP),
                "after the description's + was clicked, the text should be first in the playback order",
            ).toBe(1);
        },
    );

    await step(
        "Publish as an ePUB, and check that Play reads the text first and the description next",
        async () => {
            await expectEpubToRead(
                page,
                book.pageId,
                [book.text, book.description],
                "after the change, the text then the description",
            );
        },
    );
});

/** One recorded sentence of the page: Bloom's id for it in the Edit tab, and whose recording it has. */
interface IRecordedSentence {
    id: string;
    recording: "text" | "description";
}

/**
 * Open the ePUB screen's preview of the book, show the page `pageId`, and check that the page's
 * read-aloud lists the sentences `expectedOrder` in that order, each playing its own recording,
 * and that clicking the reader's Play reads them aloud in that order.
 */
async function expectEpubToRead(
    page: Page,
    pageId: string,
    expectedOrder: IRecordedSentence[],
    order: string,
): Promise<void> {
    await openEpubPublishScreen(page);
    await showEpubPreview(page);
    const { frame } = await showEpubPreviewPage(page, pageId);
    const expected = expectedOrder.map((sentence) => idInEpub(sentence.id));

    const clips = await getEpubPreviewPageReadAloud(page, frame);
    expect(
        clips.map((clip) => clip.textId),
        `the ePUB's read-aloud of the page should list ${order}`,
    ).toEqual(expected);

    // Bloom joins the page's recordings into one file, in the order it reads them. The text's
    // recording sounds from its start; the description's is silent for DESCRIPTION_SILENCE and
    // then sounds. So each clip's opening is loud or silent as its own recording is, and a file
    // joined in another order than the read-aloud lists would put silence under the text.
    const loudness: Record<string, { opening: number; rest?: number }> = {};
    for (const [i, clip] of clips.entries()) {
        const { recording } = expectedOrder[i];
        // Every stretch measured lies inside the clip, so only audio the reader plays for this
        // sentence counts.
        expect(
            clip.clipEnd - clip.clipBegin,
            `clip ${i + 1} of the page should last longer than the stretches measured in it`,
        ).toBeGreaterThan(
            recording === "description"
                ? DESCRIPTION_SILENCE + 0.6
                : DESCRIPTION_SILENCE,
        );
        const spans = [
            {
                start: clip.clipBegin + 0.2,
                end: clip.clipBegin + DESCRIPTION_SILENCE - 0.3,
            },
        ];
        if (recording === "description")
            spans.push({
                start: clip.clipBegin + DESCRIPTION_SILENCE + 0.3,
                end: clip.clipEnd - 0.2,
            });
        const [opening, rest] = await getEpubPreviewAudioLoudness(
            page,
            clip.audioUrl,
            spans,
        );
        loudness[recording] = { opening, rest };
    }
    const measured = `loudness, as root mean square, in the order read: ${JSON.stringify(
        clips.map((clip, i) => ({
            textId: clip.textId,
            ...loudness[expectedOrder[i].recording],
        })),
    )}`;
    const { text, description } = loudness;
    expect(
        description.rest! > description.opening * 20,
        `the description's clip should play its recording, which starts silent and then sounds (${measured})`,
    ).toBe(true);
    expect(
        text.opening > description.opening * 20,
        `the text's clip should play its recording, which sounds from its start (${measured})`,
    ).toBe(true);

    const events = await playEpubPreviewReadAlong(page, frame);
    const read: string[] = [];
    // The reader marks each sentence as it reads it; the order of those marks is the order it read.
    for (const event of events)
        if (event.kind === "highlighted" && read[read.length - 1] !== event.id)
            read.push(event.id);
    expect(
        read,
        `after Play, the ePUB preview should read aloud ${order}`,
    ).toEqual(expected);
}
