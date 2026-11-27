// An ePUB plays a page whose two text boxes each have a recording of their own, aligned to their
// sentences with Adjust Timings..., correctly: publishing joins the two recordings into the page's
// one audio file, and the ePUB screen's preview reads the page's sentences in order, each between
// the times Adjust Timings saved for it (the second box's shifted by the length of the first's),
// and highlights each sentence, and only that one, while it plays.
//
// Automates the manual test "Mixed Audio Preview Playback" (Test Case ID 167), which BL-9051 (an
// ePUB that played and highlighted such a page wrongly) came from.
//
// The case allows "a combo of imported and Bloom-recorded audio on one page (or two imported)".
// Recording needs a microphone, which a test has not got, so both boxes take theirs through the
// Talking Book tool's Import Recording, two recordings of different lengths so that one cannot
// pass for the other. The ePUB is read from the ePUB screen's preview, which shows the very files
// that Save... zips. "Plays correctly" is a person listening while the sentences light up; what
// decides it is data the test reads: which file and which part of it the reader plays for each
// sentence, and when it highlights each one.

import * as fs from "node:fs";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    epubPreviewHasFile,
    getEpubPreviewPageReadAloud,
    getEpubPreviewReadAlongHighlights,
    idInEpub,
    openEpubPublishScreen,
    playEpubPreviewReadAlong,
    showEpubPreview,
    showEpubPreviewPage,
    type EpubReadAlongEvent,
} from "../helpers/epubPublish";
import {
    acceptProposedTimings,
    importNarration,
    narrationFilePath,
    openToolboxWithTalkingBook,
    sampleNarrationFile,
} from "../helpers/talkingBook";

test.use({
    collectionSpec: {
        name: "epub-mixed-audio-playback",
        languages: ["en"],
        // Recording a whole text box, Import Recording, and publishing an ePUB need a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const L1 = "en";
const kGroup = ".bloom-translationGroup";

/** What each of the page's two text boxes says: two sentences, which Adjust Timings splits. */
const kBoxTexts = [
    ["The cat sat down.", "Then it slept."],
    ["A dog came by.", "It barked twice."],
];

/** How far, in seconds, the reader may be from a clip's boundary when it moves the highlight. */
const kStartAllowance = 0.5;
const kEndAllowance = 0.2;

type HighlightEvent = Extract<
    EpubReadAlongEvent,
    { kind: "highlighted" | "unhighlighted" }
>;
type AudioEvent = Exclude<EpubReadAlongEvent, HighlightEvent>;

/** One text box of the page as the Edit tab has it after Adjust Timings. */
interface ISplitBox {
    /** The box's id, which names its recording in audio/. */
    id: string;
    split: boolean;
    /** Each segment the reader highlights in turn: its id and text. */
    segments: { id: string; text: string }[];
    /** data-audiorecordingendtimes, as written. */
    endTimes: string;
}

/**
 * Write the suite's sample recording played `times` times over to `path`: a playable mp3 whose
 * length tells it apart from one made with another count.
 */
function writeRepeatedSample(path: string, times: number): void {
    const sample = fs.readFileSync(sampleNarrationFile);
    fs.writeFileSync(path, Buffer.concat(Array(times).fill(sample)));
}

/** The English text boxes of the page being shown, in page order. */
async function readSplitBoxes(page: Page): Promise<ISplitBox[]> {
    return editablePageFrame(page)
        .locator(`${kGroup} .bloom-editable[lang="${L1}"]`)
        .evaluateAll((boxes) =>
            boxes.map((box) => ({
                id: box.id,
                split: box.classList.contains("bloom-postAudioSplit"),
                segments: Array.from(
                    box.querySelectorAll(".bloom-highlightSegment"),
                ).map((segment) => ({
                    id: segment.id,
                    text: (segment.textContent ?? "").trim(),
                })),
                endTimes: box.getAttribute("data-audiorecordingendtimes") ?? "",
            })),
        );
}

test("an ePUB plays a page with two boxes' recordings aligned by Adjust Timings correctly [Test Case ID 167]", async ({
    page,
    step,
}, testInfo) => {
    test.setTimeout(480000);

    const recordings = [2, 3].map((times) => {
        const path = testInfo.outputPath(`sample-${times}x.mp3`);
        writeRepeatedSample(path, times);
        return path;
    });

    const book = await step(
        'Make a Basic Book with an "Image in Middle" page, and type two sentences in each of its two text boxes',
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Image in Middle");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await expect(
                editablePageFrame(page).locator(kGroup),
                "test setup: the page should have two text boxes",
            ).toHaveCount(2);
            for (const [index, sentences] of kBoxTexts.entries())
                await typeInGroup(
                    page,
                    kGroup,
                    L1,
                    sentences.join(" "),
                    index,
                    false,
                );
            await waitForSnapshotStreamIdle(page);
            return { folder, pageId: contentPage.id };
        },
    );
    if (!book) throw new Error("The book was not made.");

    const boxes = await step(
        "Import a recording for each text box, and Adjust Timings on both",
        async () => {
            await openToolboxWithTalkingBook(page);
            for (const [index, recording] of recordings.entries()) {
                await importNarration(
                    page,
                    book.folder,
                    kGroup,
                    L1,
                    recording,
                    index,
                );
                await acceptProposedTimings(page);
            }
            const split = await readSplitBoxes(page);
            const log = JSON.stringify(split);
            expect(
                split.map((box) => ({
                    split: box.split,
                    segments: box.segments.map((segment) => segment.text),
                })),
                `each box should be split into its two sentences: ${log}`,
            ).toEqual(
                kBoxTexts.map((sentences) => ({
                    split: true,
                    segments: sentences,
                })),
            );
            for (const box of split) {
                for (const segment of box.segments)
                    expect(
                        segment.id,
                        `each segment should have an id for the ePUB to name: ${log}`,
                    ).not.toBe("");
                expect(
                    fs.existsSync(narrationFilePath(book.folder, box.id)),
                    `the box ${box.id} should have its recording in the book's audio folder`,
                ).toBe(true);
            }
            expect(
                split[0].id,
                "test setup: the two boxes should have different recordings",
            ).not.toBe(split[1].id);
            return split;
        },
    );
    if (!boxes) throw new Error("The recordings were not aligned.");

    /** For each box, the times Adjust Timings saved: when each of its segments ends, in seconds. */
    const endTimes = boxes.map((box) => {
        const fields = box.endTimes.split(" ").map(Number);
        expect(
            fields.length === box.segments.length &&
                fields.every(
                    (time, i) =>
                        Number.isFinite(time) &&
                        time > (i === 0 ? 0 : fields[i - 1]),
                ),
            `Adjust Timings should save a rising end time for each segment of ${box.id}: "${box.endTimes}"`,
        ).toBe(true);
        return fields;
    });

    const shown = await step(
        "Publish as an ePUB and preview the page",
        async () => {
            await openEpubPublishScreen(page);
            await showEpubPreview(page);
            return showEpubPreviewPage(page, book.pageId);
        },
    );
    if (!shown) throw new Error("The ePUB preview never showed the page.");

    const clips = await step(
        "Check the page's read-aloud plays each sentence from its own box's recording, between the times Adjust Timings saved",
        async () => {
            const read = await getEpubPreviewPageReadAloud(page, shown.frame);
            const log = JSON.stringify(read);
            const expectedSegments = boxes.flatMap((box) =>
                box.segments.map((segment) => idInEpub(segment.id)),
            );
            expect(
                read.map((clip) => clip.textId),
                `the page should read the four sentences in order, one clip each: ${log}`,
            ).toEqual(expectedSegments);

            // Publishing joins a page's recordings into one file, in page order
            // (PublishEpubApi sets EpubMaker.OneAudioPerPage), so the second box's part of it
            // starts where the first box's last segment ends.
            const pageFile = read[0].audioUrl;
            expect(
                read.map((clip) => clip.audioUrl),
                `every sentence should play from the page's one audio file: ${log}`,
            ).toEqual(read.map(() => pageFile));
            expect(
                await epubPreviewHasFile(page, pageFile),
                `the ePUB should have the page's audio file ${pageFile}`,
            ).toBe(true);
            let offset = 0;
            boxes.forEach((box, b) => {
                box.segments.forEach((_, s) => {
                    const clip = read[b * 2 + s];
                    const begin = offset + (s === 0 ? 0 : endTimes[b][s - 1]);
                    const end = offset + endTimes[b][s];
                    expect(
                        clip.clipBegin,
                        `sentence ${s + 1} of box ${b + 1} should start where Adjust Timings ` +
                            `put the end of the one before it (${begin} s into the page's audio): ${log}`,
                    ).toBeCloseTo(begin, 2);
                    expect(
                        clip.clipEnd,
                        `sentence ${s + 1} of box ${b + 1} should end where Adjust Timings put ` +
                            `its end (${end} s into the page's audio): ${log}`,
                    ).toBeCloseTo(end, 2);
                });
                offset += endTimes[b][endTimes[b].length - 1];
            });
            return read;
        },
    );
    if (!clips) throw new Error("The page's read-aloud was not read.");

    await step(
        "Click Play, and see each sentence highlighted, in order, while its part of its recording plays",
        async () => {
            expect(
                await getEpubPreviewReadAlongHighlights(page, shown.frame),
                "before Play, nothing on the page should be highlighted",
            ).toEqual([]);

            const events = await playEpubPreviewReadAlong(page, shown.frame);
            const log = JSON.stringify(events);

            const played = events.filter(
                (e): e is AudioEvent => e.kind === "playing",
            );
            const filesPlayed = played
                .map((e) => e.audioUrl)
                .filter((url, i, all) => i === 0 || url !== all[i - 1]);
            expect(
                filesPlayed,
                `Play should play the page's audio file. What happened: ${log}`,
            ).toEqual([clips[0].audioUrl]);

            const highlights = events.filter(
                (e): e is HighlightEvent =>
                    e.kind === "highlighted" || e.kind === "unhighlighted",
            );
            expect(
                highlights
                    .filter((e) => e.kind === "highlighted")
                    .map((e) => e.id),
                `the reader should highlight each sentence once, in order. What happened: ${log}`,
            ).toEqual(clips.map((clip) => clip.textId));

            const marked = new Set<string>();
            for (const event of highlights) {
                if (event.kind === "highlighted") marked.add(event.id);
                else marked.delete(event.id);
                expect(
                    marked.size,
                    `no two sentences should be highlighted at once. What happened: ${log}`,
                ).toBeLessThanOrEqual(1);
            }

            clips.forEach((clip, i) => {
                const on = highlights.find(
                    (e) => e.kind === "highlighted" && e.id === clip.textId,
                )!;
                const off = highlights.find(
                    (e) => e.kind === "unhighlighted" && e.id === clip.textId,
                );
                const which = `sentence ${i + 1} (${clip.textId})`;
                expect(
                    on.audioPlaying && on.audioUrl === clip.audioUrl,
                    `${which} should be highlighted while its own recording plays. What happened: ${log}`,
                ).toBe(true);
                expect(
                    on.audioTime,
                    `${which} should be highlighted from where its part of the recording starts, ` +
                        `${clip.clipBegin} s. What happened: ${log}`,
                ).toBeGreaterThanOrEqual(clip.clipBegin - kEndAllowance);
                expect(
                    on.audioTime,
                    `${which} should be highlighted from where its part of the recording starts, ` +
                        `${clip.clipBegin} s. What happened: ${log}`,
                ).toBeLessThanOrEqual(clip.clipBegin + kStartAllowance);
                expect(
                    off,
                    `${which} should lose its highlight once read. What happened: ${log}`,
                ).toBeDefined();
                expect(
                    off!.audioTime,
                    `${which} should stay highlighted to the end of its part of the recording, ` +
                        `${clip.clipEnd} s. What happened: ${log}`,
                ).toBeGreaterThanOrEqual(clip.clipEnd - kEndAllowance);
            });

            const last = clips[clips.length - 1];
            const stops = events.filter(
                (e): e is AudioEvent =>
                    e.kind === "pause" || e.kind === "ended",
            );
            expect(
                stops.length,
                `the reader should stop playing at the end of the page. What happened: ${log}`,
            ).toBeGreaterThan(0);
            expect(
                stops[stops.length - 1].audioTime,
                `the reader should play the page's audio to the end of its last sentence, ` +
                    `${last.clipEnd} s. What happened: ${log}`,
            ).toBeGreaterThanOrEqual(last.clipEnd - kEndAllowance);
        },
    );
});
