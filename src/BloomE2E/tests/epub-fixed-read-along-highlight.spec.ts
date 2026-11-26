// A fixed-layout ePUB of a Talking Book reads its cover's title aloud with the title highlighted:
// the ePUB screen's preview plays the title's recording when Play is clicked, and the reader marks
// the title as the text being read for as long as it plays, with a background a reader can see.
//
// Automates the manual test "Image Descriptions ePUB Fixed" (Test Case ID 574), which turns on the
// Image Description tool before recording the title, and checks the result in a fixed-mode ePUB.
//
// The ePUB is read from the ePUB screen's preview, which shows the very files that Save... zips.
// Recording needs a microphone, which a test has not got: addNarration puts a recording under the
// id the Talking Book tool gave the title, where Bloom's own recording would go. So the case's
// audio visualizer, shown while a person speaks, is not checked.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseEpubMode,
    epubPreviewHasFile,
    getEpubMode,
    getEpubPreviewLayout,
    getEpubPreviewPageReadAloud,
    getEpubPreviewReadAlongHighlights,
    idInEpub,
    isEpubModeListEnabled,
    openEpubPublishScreen,
    playEpubPreviewReadAlong,
    showEpubPreview,
    showEpubPreviewPage,
    type EpubReadAlongEvent,
} from "../helpers/epubPublish";
import {
    kImageDescriptionGroup,
    openImageDescriptionTool,
} from "../helpers/imageDescription";
import { chooseImageFile } from "../helpers/images";
import {
    addNarration,
    getCurrentAudioSegmentText,
    getRecordingMode,
    waitForSentencesMarked,
} from "../helpers/talkingBook";
import { enableToolForBook, expandToolByHeader } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "epub-fixed-read-along-highlight",
        languages: ["en"],
        // Publishing an ePUB needs a subscription.
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
const TITLE = "The Universal Kingdom";

type HighlightEvent = Extract<
    EpubReadAlongEvent,
    { kind: "highlighted" | "unhighlighted" }
>;
type AudioEvent = Exclude<EpubReadAlongEvent, HighlightEvent>;

/** Whether a CSS color, as getComputedStyle gives it, paints anything: it is not fully transparent. */
function isPainted(color: string): boolean {
    const match = /^rgba?\(([^)]*)\)$/.exec(color.trim());
    if (!match)
        throw new Error(`"${color}" is not a color as rgb() or rgba().`);
    const parts = match[1].split(/[\s,/]+/).filter((part) => part !== "");
    return parts.length < 4 || Number(parts[3]) > 0;
}

test("a fixed-mode ePUB highlights the cover's title while it reads it aloud [Test Case ID 574]", async ({
    page,
    step,
}) => {
    test.setTimeout(480000);

    const book = await step(
        "Make a Basic Book with a picture page, turn on the Image Description tool, and type the title",
        async () => {
            const firstFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just an Image");
            await enableToolForBook(page, firstFolder, "imageDescription");
            const cover = (await getPages(page))[0];
            const [picturePage] = await getContentPages(page);
            expect(
                cover.isContentPage,
                "test setup: the first page is the cover",
            ).toBe(false);

            await goToPage(page, cover.id);
            await typeInGroup(page, ".bookTitle", L1, TITLE);

            await goToPage(page, picturePage.id);
            await chooseImageFile(page, PICTURE);
            // openImageDescriptionTool waits until the picture shows a description box beside it.
            await openImageDescriptionTool(page);
            await expect(
                editablePageFrame(page).locator(
                    `${kImageDescriptionGroup} .bloom-editable[lang="${L1}"]`,
                ),
                "with the Image Description tool open, the picture should have a description box",
            ).toBeVisible();

            // Leaving the cover saved the title, and Bloom renamed the book's folder to match it.
            return { folder: await findBookFolder(page, TITLE), cover };
        },
    );
    if (!book) throw new Error("The book was not made.");

    const titleIds = await step(
        "Open the Talking Book tool on the cover, see that it records by sentence and is on the title, and record the title",
        async () => {
            await goToPage(page, book.cover.id);
            await expandToolByHeader(page, "talkingBook");
            expect(
                await getRecordingMode(page),
                "the Talking Book tool should record by sentence unless told otherwise",
            ).toBe("By Sentence");
            const ids = await waitForSentencesMarked(page, ".bookTitle", L1, [
                TITLE,
            ]);
            expect(
                await getCurrentAudioSegmentText(page),
                "the Talking Book tool should be on the title, the cover's only text",
            ).toBe(TITLE);
            const narrated = await addNarration(page, book.folder, L1);
            expect(
                narrated,
                "test setup: the only recording on the cover is the title's",
            ).toEqual(ids);
            return ids;
        },
    );
    if (!titleIds) throw new Error("The title was not recorded.");

    const cover = await step(
        "Publish as an ePUB in Fixed mode and preview it",
        async () => {
            await openEpubPublishScreen(page);
            expect(
                await isEpubModeListEnabled(page),
                'the "ePUB mode" list should be enabled',
            ).toBe(true);
            await chooseEpubMode(page, "fixed");
            expect(await getEpubMode(page)).toBe("fixed");
            await showEpubPreview(page);
            expect(
                await getEpubPreviewLayout(page),
                "the ePUB being previewed should be a fixed-layout one",
            ).toBe("fixed");
            return showEpubPreviewPage(page, book.cover.id);
        },
    );
    if (!cover) throw new Error("The ePUB preview never showed the cover.");

    await step(
        "Click Play over the cover, and see the title highlighted while its recording plays",
        async () => {
            const clips = await getEpubPreviewPageReadAloud(page, cover.frame);
            expect(
                clips.map((clip) => clip.textId),
                "test setup: the cover's read-aloud should be the title's recording alone",
            ).toEqual(titleIds.map(idInEpub));
            const titleId = clips[0].textId;
            expect(
                await epubPreviewHasFile(page, clips[0].audioUrl),
                `test setup: the ePUB should have the title's audio file, ${clips[0].audioUrl}`,
            ).toBe(true);
            expect(
                await cover.frame
                    .locator(`[id="${titleId}"]`)
                    .evaluate((element) => element.textContent?.trim()),
                "test setup: the title's clip should be the title's text in the ePUB",
            ).toBe(TITLE);
            expect(
                await getEpubPreviewReadAlongHighlights(page, cover.frame),
                "before Play, nothing on the cover should be highlighted",
            ).toEqual([]);

            const events = await playEpubPreviewReadAlong(page, cover.frame);
            const log = JSON.stringify(events);

            const played = events.filter(
                (e): e is AudioEvent => e.kind === "playing",
            );
            expect(
                played.map((e) => e.audioUrl),
                `Play should play the title's recording, once. What happened: ${log}`,
            ).toEqual([clips[0].audioUrl]);

            const highlightEvents = events.filter(
                (e): e is HighlightEvent =>
                    e.kind === "highlighted" || e.kind === "unhighlighted",
            );
            expect(
                highlightEvents.map((e) => `${e.kind} ${e.id}`),
                `the reader should highlight the title, and only the title, once, and then take ` +
                    `the highlight off. What happened: ${log}`,
            ).toEqual([`highlighted ${titleId}`, `unhighlighted ${titleId}`]);

            const [on, off] = highlightEvents;
            if (on.kind !== "highlighted" || off.kind !== "unhighlighted")
                throw new Error("Unreachable: the kinds were checked above.");
            expect(
                isPainted(on.background),
                `the highlighted title should have a background that shows, not ${on.background}`,
            ).toBe(true);
            // The highlight goes on as the clip starts playing, and comes off only once it has
            // played to its end: it covers the whole of the title's recording.
            expect(
                clips[0].clipEnd - clips[0].clipBegin,
                "test setup: the title's clip should last long enough for its start and end to differ",
            ).toBeGreaterThan(1);
            expect(
                on.audioPlaying,
                `the title should be highlighted while its recording plays. What happened: ${log}`,
            ).toBe(true);
            expect(
                on.audioTime,
                `the title should be highlighted from the start of its recording. What happened: ${log}`,
            ).toBeLessThanOrEqual(clips[0].clipBegin + 0.5);
            expect(
                off.audioTime,
                `the title should stay highlighted to the end of its recording, ` +
                    `${clips[0].clipEnd} s. What happened: ${log}`,
            ).toBeGreaterThanOrEqual(clips[0].clipEnd - 0.2);
        },
    );
});
