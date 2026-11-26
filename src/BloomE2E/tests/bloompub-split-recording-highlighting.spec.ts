// A text box recorded as a whole and split into sentences with Adjust Timings... plays in the
// BloomPUB preview one sentence at a time: Bloom Player highlights each sentence while its part of
// the recording plays, in order, and moves the highlight on when the recording reaches the end
// time Adjust Timings saved for it.
//
// Automates the manual test "Split Recording Sentence Highlighting" (Test Case ID 501).
//
// A person records the box with a microphone; the test puts a recording on it with the Talking
// Book tool's Import Recording, the one way Bloom takes a whole-text-box recording without one.
// What a person watches for is the highlight moving from sentence to sentence as the recording
// plays. Bloom Player shows it by giving the segment being read the class ui-audioCurrent, which
// its stylesheet paints; the test notes, from inside the preview, each segment that gets it, when
// in the recording it gets it, and whether it is painted differently from the sentences around
// it.

import * as fs from "node:fs";
import type { Frame } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { goToPreviewPage } from "../helpers/bloomPlayer";
import { bookHtmlPath } from "../helpers/bookHtml";
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
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    acceptProposedTimings,
    importNarration,
    openToolboxWithTalkingBook,
    sampleNarrationFile,
} from "../helpers/talkingBook";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "bloompub-split-recording-highlighting",
        languages: ["en"],
        // Recording a whole text box and Import Recording both need a Pro subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kGroup = ".bloom-translationGroup";

/** The box's three sentences, typed as one paragraph. */
const kSentences = [
    "The king had a golden cup.",
    "One day it fell into the river.",
    "A fish swam away with it.",
];

/**
 * How many copies of the sample recording make up the box's recording. One copy lasts about a
 * second, too short to see three sentences highlighted in turn with any margin.
 */
const kCopiesOfSample = 6;

/**
 * One moment at which Bloom Player changed which part of the page it highlights, or, with `ended`,
 * the moment the narration finished playing.
 */
interface IHighlightChange {
    /** True for the entry that says the narration reached its end. */
    ended: boolean;
    /** The text of each element Bloom Player marked ui-audioCurrent, trimmed. */
    highlighted: string[];
    /** Whether each highlighted element is a segment of the split box (bloom-highlightSegment). */
    segments: boolean[];
    /**
     * Whether each highlighted element is painted with a background different from that of every
     * other segment of its box, which is what makes the highlight visible. A transparent element
     * shows whatever is behind it, so each is compared by the first background that is not
     * transparent, from it outwards.
     */
    paintedApart: boolean[];
    /** How far Bloom Player's narration player had played, in seconds. */
    time: number;
    /** The file name of the recording the narration player holds. */
    recording: string;
}

/**
 * Start noting, in the preview, every change in what Bloom Player highlights, with how far its
 * narration player had played (see IHighlightChange), and the moment that player reaches the end
 * of a recording. The record lives in the player's document.
 */
async function startRecordingHighlights(player: Frame): Promise<void> {
    await player.evaluate(() => {
        const w = window as unknown as { e2eHighlights?: unknown[] };
        const changes: unknown[] = [];
        w.e2eHighlights = changes;
        // Bloom Player plays narration through one audio element, which it makes when it first
        // shows a page with narration (bloom-player's narration.ts).
        const playing = () => {
            const narration = document.getElementById(
                "bloom-audio-player",
            ) as HTMLAudioElement | null;
            return {
                time: narration?.currentTime ?? -1,
                recording: decodeURIComponent(
                    (narration?.currentSrc || narration?.src || "")
                        .split("?")[0]
                        .split("/")
                        .pop() ?? "",
                ),
            };
        };
        const shownBackground = (element: Element): string => {
            for (let e: Element | null = element; e; e = e.parentElement) {
                const color = getComputedStyle(e).backgroundColor;
                if (
                    color !== "transparent" &&
                    !/^rgba\([^)]*,\s*0\)$/.test(color)
                )
                    return color;
            }
            return "none";
        };
        let last = "";
        const note = () => {
            const current = Array.from(
                document.querySelectorAll<HTMLElement>(
                    ".swiper-slide-active .ui-audioCurrent",
                ),
            );
            const shown = {
                highlighted: current.map((element) =>
                    (element.textContent ?? "").trim(),
                ),
                segments: current.map((element) =>
                    element.classList.contains("bloom-highlightSegment"),
                ),
                paintedApart: current.map((element) => {
                    const box = element.closest(".bloom-editable");
                    if (!box) return false;
                    const background = shownBackground(element);
                    const others = Array.from(
                        box.querySelectorAll<HTMLElement>(
                            ".bloom-highlightSegment",
                        ),
                    ).filter((other) => other !== element);
                    return (
                        others.length > 0 &&
                        others.every(
                            (other) => shownBackground(other) !== background,
                        )
                    );
                }),
            };
            // A change in how the highlight is painted is noted too, not only a move.
            const key = JSON.stringify(shown);
            if (key === last) return;
            last = key;
            changes.push({ ended: false, ...shown, ...playing() });
        };
        new MutationObserver(note).observe(document.body, {
            attributes: true,
            attributeFilter: ["class"],
            subtree: true,
        });
        // A media element's events do not bubble, so this one is caught on its way down.
        document.addEventListener(
            "ended",
            (e) => {
                if ((e.target as Element | null)?.id !== "bloom-audio-player")
                    return;
                changes.push({
                    ended: true,
                    highlighted: [],
                    segments: [],
                    paintedApart: [],
                    ...playing(),
                });
            },
            true,
        );
    });
}

async function getHighlightChanges(player: Frame): Promise<IHighlightChange[]> {
    return player.evaluate(
        () =>
            (window as unknown as { e2eHighlights: IHighlightChange[] })
                .e2eHighlights,
    );
}

/** The saved English text box of the book's one content page. */
async function readSavedBox(
    player: Frame,
    bookFolder: string,
): Promise<{
    /** The box's id, which names its recording in audio/. */
    id: string;
    split: boolean;
    recordingMode: string;
    segments: string[];
    endTimes: number[];
}> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return player.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        const box = dom.querySelector<HTMLElement>(
            '.bloom-page:not(.bloom-frontMatter):not(.bloom-backMatter) .bloom-translationGroup .bloom-editable[lang="en"]',
        )!;
        return {
            id: box.id,
            split: box.classList.contains("bloom-postAudioSplit"),
            recordingMode: box.getAttribute("data-audiorecordingmode") ?? "",
            segments: Array.from(
                box.querySelectorAll(".bloom-highlightSegment"),
            ).map((segment) => (segment.textContent ?? "").trim()),
            endTimes: (box.getAttribute("data-audiorecordingendtimes") ?? "")
                .split(" ")
                .map(Number),
        };
    }, html);
}

test("a recording split into sentences is highlighted sentence by sentence in the BloomPUB preview [Test Case ID 501]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;

    const recording = testInfo.outputPath("narration.mp3");
    fs.mkdirSync(testInfo.outputDir, { recursive: true });
    const sample = fs.readFileSync(sampleNarrationFile);
    fs.writeFileSync(
        recording,
        Buffer.concat(Array(kCopiesOfSample).fill(sample)),
    );

    const made = await step(
        "Record a text box as a whole and split it into sentences with Adjust Timings",
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text", 1);
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await typeInGroup(
                page,
                kGroup,
                "en",
                kSentences.join(" "),
                0,
                false,
            );
            await expect(
                editablePageFrame(page)
                    .locator(`${kGroup} .bloom-editable[lang="en"]`)
                    .first(),
            ).toHaveText(kSentences.join(" "), {
                timeout: 15000,
                useInnerText: true,
            });
            await waitForSnapshotStreamIdle(page);

            await openToolboxWithTalkingBook(page);
            await importNarration(page, bookFolder, kGroup, "en", recording);
            await acceptProposedTimings(page);
            // Leaving the Edit tab saves the book.
            await switchTab(page, "collection");
            return { bookFolder, pageId: contentPage.id };
        },
    );
    if (!made) throw new Error("The book was not made.");

    const preview = await step(
        "Publish to BloomPUB and show the page in the preview",
        async () => {
            await openPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            return player;
        },
    );
    if (!preview) throw new Error("The BloomPUB preview never opened.");

    const saved = await readSavedBox(preview, made.bookFolder);
    await step("Check the saved box is split into its sentences", () => {
        expect(
            {
                split: saved.split,
                recordingMode: saved.recordingMode,
                segments: saved.segments,
                endTimeCount: saved.endTimes.length,
            },
            "Adjust Timings should have split the whole-box recording at each sentence end",
        ).toEqual({
            split: true,
            recordingMode: "TextBox",
            segments: kSentences,
            endTimeCount: kSentences.length,
        });
        saved.endTimes.forEach((end, index) =>
            expect(
                end,
                `sentence ${index + 1} should end after the one before it: ${saved.endTimes.join(" ")}`,
            ).toBeGreaterThan(index === 0 ? 0 : saved.endTimes[index - 1]),
        );
    });

    await step(
        "Play the page and see each sentence highlighted in turn",
        async () => {
            await startRecordingHighlights(preview);
            // goToPreviewPage waits for Bloom Player to settle before turning the page. A page
            // turned to before then is shown without Bloom Player learning that it has narration,
            // so it offers no Play button and plays nothing.
            await goToPreviewPage(page, preview, made.pageId);
            // Bloom Player plays a page's narration by itself once it shows the page, as it does
            // for a reader.
            //
            await expect
                .poll(
                    async () =>
                        (await getHighlightChanges(preview)).some(
                            (change) => change.ended,
                        ),
                    {
                        timeout: 60000,
                        message:
                            "Bloom Player never played the page's narration to its end.",
                    },
                )
                .toBe(true);
            const all = await getHighlightChanges(preview);
            const end = all.findIndex((change) => change.ended);
            const start = all.findIndex(
                (change) => change.highlighted.length > 0,
            );
            expect(
                all[end].recording,
                "the narration that played should be the box's recording",
            ).toBe(`${saved.id}.mp3`);
            expect(
                start >= 0 && start < end,
                "Bloom Player should have highlighted something before the narration ended",
            ).toBe(true);
            // From the first highlight to the end of the recording, the highlight never leaves
            // the text: every move takes it to the next sentence. Bloom Player first marks a
            // sentence and then, as the recording starts playing, stops holding its paint back
            // (ui-suppressHighlight), so one sentence can have several entries in a row; they
            // are taken together as one turn.
            const between = all.slice(start, end);
            const turns: IHighlightChange[][] = [];
            for (const change of between) {
                const previous = turns.at(-1);
                if (
                    previous &&
                    JSON.stringify(previous[0].highlighted) ===
                        JSON.stringify(change.highlighted)
                )
                    previous.push(change);
                else turns.push([change]);
            }

            expect(
                turns.map((turn) => turn[0].highlighted),
                "Bloom Player should highlight the sentences one at a time, in order, with no gap and nothing else, until the recording ends",
            ).toEqual(kSentences.map((sentence) => [sentence]));
            turns.forEach((turn, index) => {
                turn.forEach((change) =>
                    expect(
                        change.recording,
                        `"${kSentences[index]}" should be highlighted while the box's recording plays`,
                    ).toBe(`${saved.id}.mp3`),
                );
                expect(
                    turn[0].segments,
                    `"${kSentences[index]}" should be highlighted as a segment of the split box`,
                ).toEqual([true]);
                // The highlight must show as the sentence starts and stay until it ends: from the
                // first painted entry on, every entry of the turn is painted, and that entry
                // comes within half a second of the sentence's turn starting.
                const firstPainted = turn.findIndex(
                    (change) => change.paintedApart[0] === true,
                );
                expect(
                    firstPainted,
                    `"${kSentences[index]}" should be painted differently from the other sentences while it plays`,
                ).toBeGreaterThanOrEqual(0);
                expect(
                    turn
                        .slice(firstPainted)
                        .map((change) => change.paintedApart),
                    `"${kSentences[index]}" should stay painted until the next sentence takes over`,
                ).toEqual(turn.slice(firstPainted).map(() => [true]));
                expect(
                    turn[firstPainted].time - turn[0].time,
                    `"${kSentences[index]}" should be painted within half a second of its turn starting`,
                ).toBeLessThan(0.5);
            });
            expect(
                turns[0][0].time,
                "the first sentence should be highlighted before the recording has played",
            ).toBe(0);
            // Each sentence after the first is highlighted when the recording reaches the end of
            // the one before it. Bloom Player moves the highlight on a timer it sets from the
            // recording's position, so it may be a little late, never early.
            for (let index = 1; index < turns.length; index++) {
                const previousEnd = saved.endTimes[index - 1];
                expect(
                    turns[index][0].time,
                    `"${kSentences[index]}" should be highlighted once the recording reaches ${previousEnd} s, where the sentence before it ends`,
                ).toBeGreaterThanOrEqual(previousEnd - 0.05);
                expect(
                    turns[index][0].time,
                    `"${kSentences[index]}" should be highlighted soon after the recording reaches ${previousEnd} s`,
                ).toBeLessThan(previousEnd + 0.5);
            }
            // The last sentence is still highlighted when the recording ends, at its saved end.
            expect(
                Math.abs(all[end].time - saved.endTimes.at(-1)!),
                `the recording should end where the last sentence does, ${saved.endTimes.at(-1)} s, not at ${all[end].time} s`,
            ).toBeLessThan(0.25);
        },
    );
});
