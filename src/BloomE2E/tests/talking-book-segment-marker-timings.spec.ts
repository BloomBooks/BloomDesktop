// A text box recorded as a whole, whose timings were adjusted once, can be split further with
// segment markers ("|") typed on the keyboard or put in with the Talking Book tool's Insert Segment
// Marker button; then Adjust Timings... > More > Re-run pause-based timing guesses splits the
// recording at those markers and at the sentence ends, each split falling in a pause of the
// recording.
//
// Automates the manual test "Add Segment Markers Timings" (Test Case ID 516).
//
// A person records the box with a microphone; the test puts a recording on it with the Talking
// Book tool's Import Recording, the one way Bloom takes a whole-text-box recording without one.
// The recording is made of sound with a pause after each part of the text, so that where the
// pauses are is known. What the person confirms in the dialog is that its sections follow both the
// markers and the pauses; the test reads the same sections from what OK saves on the box (its
// segments, and data-audiorecordingendtimes, the end time of each), and checks that each section
// ends inside a pause.
//
// The case notes that reopening Adjust Timings ought to show the new segments without the re-run
// (BL-15678); the test does not check what the dialog shows before the re-run.

import * as fs from "node:fs";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    acceptProposedTimings,
    clickInsertSegmentMarker,
    clickOkInAdjustTimings,
    findPausesInRecording,
    importNarration,
    openAdjustTimingsDialog,
    openToolboxWithTalkingBook,
    rerunPauseBasedTimingGuesses,
    sampleNarrationFile,
    silentMp3Frames,
} from "../helpers/talkingBook";

test.use({
    collectionSpec: {
        name: "talking-book-segment-marker-timings",
        languages: ["en"],
        // Recording a whole text box and Import Recording both need a Pro subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kGroup = ".bloom-translationGroup";

/** The box's two sentences, typed as one paragraph. */
const kFirstSentence = "Ducks swim in the green pond.";
const kSecondSentence = "Frogs sit on the lily pads.";

/** Where the keyboard's marker goes: before this part of the first sentence. */
const kTypedMarkerBefore = "the green pond.";
/** Where Insert Segment Marker puts its marker: before this part of the second sentence. */
const kButtonMarkerBefore = "the lily pads.";

/** The segments the two markers and the sentence end cut the text into. */
const kSegments = [
    "Ducks swim in",
    "the green pond.",
    "Frogs sit on",
    "the lily pads.",
];

/**
 * How many copies of the sample recording (about a second each) make up the sound of each
 * segment. The lengths differ from those of the segments' text, so a split guessed from the text
 * alone would not fall in the pauses.
 */
const kCopiesPerSegment = [2, 3, 2, 3];

/** The length of the pause after each segment but the last, in seconds. */
const kPauseSeconds = 0.6;

/** A segment's text as the reader reads it: without its marker, its spaces collapsed. */
function readable(text: string): string {
    return text.replace(/\|/g, "").replace(/\s+/g, " ").trim();
}

test("segment markers added after timing split the recording at them and at its pauses [Test Case ID 516]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;

    const recording = testInfo.outputPath("narration.mp3");
    fs.mkdirSync(testInfo.outputDir, { recursive: true });
    const sample = fs.readFileSync(sampleNarrationFile);
    const parts: Buffer[] = [];
    kCopiesPerSegment.forEach((copies, i) => {
        parts.push(...Array(copies).fill(sample));
        if (i < kCopiesPerSegment.length - 1)
            parts.push(silentMp3Frames(kPauseSeconds));
    });
    fs.writeFileSync(recording, Buffer.concat(parts));

    const { duration, pauses } = await findPausesInRecording(
        page,
        recording,
        kPauseSeconds / 2,
    );
    expect(
        pauses.length,
        `The test's recording should have a pause after each segment but the last; it has ${JSON.stringify(pauses)}.`,
    ).toBe(kSegments.length - 1);

    await step(
        'Record the text "By whole text", then open Adjust Timings and close it',
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text", 1);
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await typeInGroup(
                page,
                kGroup,
                "en",
                `${kFirstSentence} ${kSecondSentence}`,
                0,
                false,
            );
            await waitForSnapshotStreamIdle(page);
            await openToolboxWithTalkingBook(page);
            await importNarration(page, bookFolder, kGroup, "en", recording);
            await acceptProposedTimings(page);
        },
    );

    const box = editablePageFrame(page)
        .locator(`${kGroup} .bloom-editable[lang="en"]`)
        .first();

    await step("Type a segment marker in the first sentence", async () => {
        await clickInGroup(page, kGroup, "en");
        await page.keyboard.press("Control+Home");
        const offset = kFirstSentence.indexOf(kTypedMarkerBefore);
        for (let i = 0; i < offset; i++)
            await page.keyboard.press("ArrowRight");
        await page.keyboard.type("|");
        await expect(box).toContainText(`in |${kTypedMarkerBefore}`, {
            timeout: 15000,
            useInnerText: true,
        });
    });

    await step(
        "Put a segment marker in the second sentence with Advanced > Insert Segment Marker",
        async () => {
            await clickInGroup(page, kGroup, "en");
            await page.keyboard.press("Control+End");
            for (let i = 0; i < kButtonMarkerBefore.length; i++)
                await page.keyboard.press("ArrowLeft");
            await clickInsertSegmentMarker(page);
            await expect(box).toContainText(`on |${kButtonMarkerBefore}`, {
                timeout: 15000,
                useInnerText: true,
            });
        },
    );

    await step(
        "Open Adjust Timings again, re-run the pause-based timing guesses and press OK",
        async () => {
            await clickInGroup(page, kGroup, "en");
            const dialog = await openAdjustTimingsDialog(page);
            await rerunPauseBasedTimingGuesses(page, dialog);
            await clickOkInAdjustTimings(dialog);
        },
    );

    await step(
        "Verify: the sections are split at both the segment markers and the pauses",
        async () => {
            const saved = await box.evaluate((element) => ({
                segments: Array.from(
                    element.querySelectorAll(".bloom-highlightSegment"),
                ).map((s) => s.textContent ?? ""),
                endTimes:
                    element.getAttribute("data-audiorecordingendtimes") ?? "",
            }));
            expect(
                saved.segments.map(readable),
                "The sections should be split at the typed marker, the sentence end and the inserted marker.",
            ).toEqual(kSegments);
            const endTimes = saved.endTimes.split(" ").map(Number);
            expect(
                endTimes.length,
                `There should be an end time for each section; data-audiorecordingendtimes is "${saved.endTimes}".`,
            ).toBe(kSegments.length);
            // Every section but the last ends inside a pause of its own, in order.
            pauses.forEach((pause, i) => {
                expect(
                    endTimes[i],
                    `Section ${i + 1} ("${kSegments[i]}") should end in the pause from ${pause.start.toFixed(2)} s to ${pause.end.toFixed(2)} s of the ${duration.toFixed(2)} s recording; the end times are ${saved.endTimes}.`,
                ).toBeGreaterThanOrEqual(pause.start);
                expect(
                    endTimes[i],
                    `Section ${i + 1} ("${kSegments[i]}") should end in the pause from ${pause.start.toFixed(2)} s to ${pause.end.toFixed(2)} s of the ${duration.toFixed(2)} s recording; the end times are ${saved.endTimes}.`,
                ).toBeLessThanOrEqual(pause.end);
            });
        },
    );
});
