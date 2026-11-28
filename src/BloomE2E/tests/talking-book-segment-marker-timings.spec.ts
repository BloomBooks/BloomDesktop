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
// markers and the pauses; the test reads the sections the dialog shows over the waveform, checks
// their text and that each ends inside a pause, then presses OK and checks that the box keeps the
// same sections and times (its segments, and data-audiorecordingendtimes).
//
// The case notes that reopening Adjust Timings ought to show the new segments without the re-run
// (BL-15678); the test does not check what the dialog shows before the re-run.

import * as fs from "node:fs";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    acceptProposedTimings,
    clickOkInAdjustTimings,
    findPausesInRecording,
    importNarration,
    insertSegmentMarkerBefore,
    openAdjustTimingsDialog,
    openToolboxWithTalkingBook,
    readAdjustTimingsRegions,
    readSegmentTimings,
    rerunPauseBasedTimingGuesses,
    sampleNarrationFile,
    silentMp3Frames,
    typeSegmentMarkerBefore,
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

    await step("Type a segment marker in the first sentence", async () => {
        await typeSegmentMarkerBefore(page, kGroup, "en", kTypedMarkerBefore);
    });

    await step(
        "Put a segment marker in the second sentence with Advanced > Insert Segment Marker",
        async () => {
            await insertSegmentMarkerBefore(
                page,
                kGroup,
                "en",
                kButtonMarkerBefore,
            );
        },
    );

    /** Where `seconds` falls among the pauses, for failure messages. */
    const describePauses = () =>
        `the pauses of the ${duration.toFixed(2)} s recording are ` +
        pauses
            .map((p) => `${p.start.toFixed(2)}-${p.end.toFixed(2)} s`)
            .join(", ");

    const shown = await step(
        "Open Adjust Timings again and re-run the pause-based timing guesses",
        async () => {
            await clickInGroup(page, kGroup, "en");
            const dialog = await openAdjustTimingsDialog(page);
            await rerunPauseBasedTimingGuesses(page, dialog);
            return { dialog, regions: await readAdjustTimingsRegions(dialog) };
        },
    );
    if (!shown) throw new Error("The Adjust Timings dialog was not opened.");

    const shownEnds = await step(
        "Verify in the dialog: the sections are split at both the segment markers and the pauses",
        async () => {
            const { regions } = shown;
            expect(
                regions.map((r) => readable(r.text)),
                "The dialog should show the sections split at the typed marker, the sentence end and the inserted marker.",
            ).toEqual(kSegments);
            const ends = regions.map((r) => r.end * duration);
            pauses.forEach((pause, i) => {
                expect(
                    ends[i] >= pause.start && ends[i] <= pause.end,
                    `In the dialog, section ${i + 1} ("${kSegments[i]}") should end in pause ${i + 1}; it ends at ${ends[i].toFixed(2)} s, and ${describePauses()}.`,
                ).toBe(true);
                expect(
                    regions[i + 1].start,
                    `In the dialog, section ${i + 2} should start where section ${i + 1} ends.`,
                ).toBeCloseTo(regions[i].end, 3);
            });
            return ends;
        },
    );
    if (!shownEnds) throw new Error("The dialog's sections were not read.");

    await step(
        "Press OK: the box keeps the sections and the times the dialog showed",
        async () => {
            await clickOkInAdjustTimings(shown.dialog);
            const saved = await readSegmentTimings(page, kGroup, "en");
            expect(
                saved.segments.map(readable),
                "OK should keep the box split at the typed marker, the sentence end and the inserted marker.",
            ).toEqual(kSegments);
            const endTimes = saved.endTimes.split(" ").map(Number);
            expect(
                endTimes.length,
                `There should be an end time for each section; data-audiorecordingendtimes is "${saved.endTimes}".`,
            ).toBe(kSegments.length);
            endTimes.forEach((end, i) => {
                expect(
                    Number.isFinite(end) && end > (endTimes[i - 1] ?? 0),
                    `Each section should end, as a number of seconds, after the one before it; data-audiorecordingendtimes is "${saved.endTimes}".`,
                ).toBe(true);
            });
            pauses.forEach((pause, i) => {
                expect(
                    endTimes[i] >= pause.start && endTimes[i] <= pause.end,
                    `Section ${i + 1} ("${kSegments[i]}") should end in pause ${i + 1}; data-audiorecordingendtimes is "${saved.endTimes}", and ${describePauses()}.`,
                ).toBe(true);
                expect(
                    endTimes[i],
                    `OK should save the end of section ${i + 1} that the dialog showed.`,
                ).toBeCloseTo(shownEnds[i], 1);
            });
            const last = endTimes[endTimes.length - 1];
            expect(
                last >= pauses[pauses.length - 1].end && last <= duration + 0.1,
                `The last section should end at the end of the recording, after the last pause; data-audiorecordingendtimes is "${saved.endTimes}", and ${describePauses()}.`,
            ).toBe(true);
        },
    );
});
