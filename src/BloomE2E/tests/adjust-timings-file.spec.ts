// The Adjust Timings dialog of the Talking Book tool: its playback follows the boundaries between
// segments, including one a person drags; Edit Timings File... writes those boundaries to the box's
// timings file; Apply Timings File... offers that file and shows the times read from it; and after
// OK, the tool's Check plays the box with each segment highlighted from the time the file gave it.
//
// Automates the manual test "Adjust Timings and the Timings File" (Test Case ID 519).
//
// A person records the box with a microphone; the test puts a recording on it with the tool's
// Import Recording, the one way Bloom takes a whole-text-box recording without one. A person edits
// the timings file in the program that opens it; the test edits the file on disk, and answers
// Bloom's request to open the file in that program itself (after checking that it names the
// timings file), so that no editor window is left open on the machine. The file chooser of Apply
// Timings File... is answered through Bloom's e2e hook (e2e/nextFileToChoose), after the test has
// noted which file Bloom asked it to start at.
//
// The dialog plays the recording through WaveSurfer, which plays a segment by seeking to its start
// and pausing once the recording passes its end. The test notes each play and pause of that audio,
// with how far it had played, by wrapping HTMLMediaElement's play() and pause() in the dialog's
// page. The tool highlights the segment being read through the CSS highlight registry
// (bloom-audio-current), which the test watches in the same way, from inside the page being
// edited, against how far the tool's own player had played.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Frame, Locator, Page, Request } from "@playwright/test";
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
import { armFileChooser } from "../helpers/nativeDialogs";
import {
    importNarration,
    openToolboxWithTalkingBook,
    sampleNarrationFile,
} from "../helpers/talkingBook";
import { getUiStrings } from "../helpers/uiLanguage";

test.use({
    collectionSpec: {
        name: "adjust-timings-file",
        languages: ["en"],
        // Recording a whole text box and Import Recording both need a Pro subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kGroup = ".bloom-translationGroup";

/** The box's three sentences, typed as one paragraph; the dialog makes one segment of each. */
const kSentences = [
    "The king had a golden cup.",
    "One day it fell into the river.",
    "A fish swam away with it.",
];

/**
 * How many copies of the sample recording make up the box's recording. One copy lasts about a
 * second, too short for three segments with room to move their boundaries.
 */
const kCopiesOfSample = 6;

/**
 * How late, in seconds, the dialog may stop a segment after the recording passes the segment's end.
 * WaveSurfer checks the position as the audio reports its progress, a few times a second.
 */
const kStopLateness = 0.5;

/** One play() or pause() of an audio element in the shell page, where the dialog is. */
interface IMediaEvent {
    what: "play" | "pause";
    /** How far the audio had played, in seconds. */
    time: number;
    /** The recording's length, in seconds (NaN before it has loaded). */
    duration: number;
}

/** Start noting every play() and pause() of an audio element in the shell page. */
async function startNotingPlayback(page: Page): Promise<void> {
    await page.evaluate(() => {
        const w = window as unknown as {
            e2eMediaEvents?: unknown[];
            e2eMedia?: HTMLMediaElement;
        };
        if (w.e2eMediaEvents) return;
        const events: unknown[] = [];
        w.e2eMediaEvents = events;
        const proto = HTMLMediaElement.prototype;
        const play = proto.play;
        const pause = proto.pause;
        proto.play = function (this: HTMLMediaElement) {
            w.e2eMedia = this;
            events.push({
                what: "play",
                time: this.currentTime,
                duration: this.duration,
            });
            return play.call(this);
        };
        proto.pause = function (this: HTMLMediaElement) {
            events.push({
                what: "pause",
                time: this.currentTime,
                duration: this.duration,
            });
            return pause.call(this);
        };
    });
}

/** How far the audio that played last has played, in seconds, and whether it is paused. */
async function getPlayingPosition(
    page: Page,
): Promise<{ time: number; paused: boolean }> {
    return page.evaluate(() => {
        const media = (window as unknown as { e2eMedia: HTMLMediaElement })
            .e2eMedia;
        return { time: media.currentTime, paused: media.paused };
    });
}

async function getPlayback(page: Page): Promise<IMediaEvent[]> {
    return page.evaluate(
        () =>
            (window as unknown as { e2eMediaEvents: IMediaEvent[] })
                .e2eMediaEvents,
    );
}

/** The dialog's segments, as the boundaries it shows: each one's start and end, in seconds. */
async function getShownSegments(
    page: Page,
    duration: number,
): Promise<{ text: string; start: number; end: number }[]> {
    const shares = await page.evaluate(() => {
        const root =
            document.getElementById("waveform")!.firstElementChild!.shadowRoot!;
        return Array.from(
            root.querySelectorAll<HTMLElement>('[part~="region"]'),
        ).map((region) => ({
            text:
                region
                    .querySelector('[part~="region-content"]')
                    ?.textContent?.trim() ?? "",
            // WaveSurfer places each region by the share of the recording before it and after it.
            left: parseFloat(region.style.left),
            right: parseFloat(region.style.right),
        }));
    });
    return shares.map((share) => ({
        text: share.text,
        start: (share.left / 100) * duration,
        end: ((100 - share.right) / 100) * duration,
    }));
}

/**
 * Click one segment of the dialog's waveform, away from its boundaries and its play button, and
 * return how many play() and pause() calls had been noted before the click.
 */
async function clickSegment(
    page: Page,
    dialog: Locator,
    index: number,
): Promise<number> {
    const before = (await getPlayback(page)).length;
    await dialog
        .locator('#waveform [part~="region-content"]')
        .nth(index)
        .click();
    return before;
}

/** Wait for the play() and, when `toEnd`, the pause() that a click on a segment started. */
async function waitForPlayback(
    page: Page,
    after: number,
    count: number,
    what: string,
): Promise<IMediaEvent[]> {
    await expect
        .poll(async () => (await getPlayback(page)).length - after, {
            timeout: 30000,
            message: what,
        })
        .toBeGreaterThanOrEqual(count);
    return (await getPlayback(page)).slice(after);
}

/** One change in what the Talking Book tool highlights as current on the page being edited. */
interface IHighlightChange {
    /** The text the highlight covers, trimmed; empty when it was taken away. */
    text: string;
    /** How far the tool's player had played, in seconds. */
    time: number;
}

/**
 * Start noting, in the page being edited, every change to the tool's bloom-audio-current
 * highlight, with how far the tool's player (an audio element in the toolbox) had played, and the
 * moment that player reaches the end of the recording.
 */
async function startNotingHighlights(pageFrame: Frame): Promise<void> {
    await pageFrame.evaluate(() => {
        const w = window as unknown as {
            e2eHighlights?: unknown[];
            e2eEnded?: number;
        };
        const changes: unknown[] = [];
        w.e2eHighlights = changes;
        // The tool keeps its player in the document its own code runs in; look in each frame of
        // the Edit tab for it.
        const player = (): HTMLMediaElement | null => {
            const top = window.top!;
            const windows: Window[] = [top];
            for (let i = 0; i < top.frames.length; i++)
                windows.push(top.frames[i]);
            for (const w of windows) {
                const found = w.document.getElementById("player");
                if (found?.tagName === "AUDIO")
                    return found as HTMLMediaElement;
            }
            return null;
        };
        (w as unknown as { e2ePlayer: typeof player }).e2ePlayer = player;
        const registry = CSS.highlights as unknown as {
            set: (name: string, highlight: Iterable<Range>) => unknown;
            delete: (name: string) => boolean;
        };
        const proto = Object.getPrototypeOf(registry);
        const set = proto.set;
        const del = proto.delete;
        proto.set = function (name: string, highlight: Iterable<Range>) {
            if (name === "bloom-audio-current")
                changes.push({
                    text: Array.from(highlight)
                        .map((range) => range.toString())
                        .join("")
                        .trim(),
                    time: player()?.currentTime ?? -1,
                });
            return set.call(this, name, highlight);
        };
        proto.delete = function (name: string) {
            if (name === "bloom-audio-current")
                changes.push({ text: "", time: player()?.currentTime ?? -1 });
            return del.call(this, name);
        };
    });
}

/** The highlight changes noted so far, and whether the tool's player has played to its end. */
async function getHighlights(
    pageFrame: Frame,
): Promise<{ changes: IHighlightChange[]; ended: boolean }> {
    return pageFrame.evaluate(() => {
        const w = window as unknown as {
            e2eHighlights: IHighlightChange[];
            e2ePlayer: () => HTMLMediaElement | null;
        };
        return {
            changes: w.e2eHighlights,
            ended: w.e2ePlayer()?.ended ?? false,
        };
    });
}

/**
 * Open the dialog's More menu and choose one of its items, found by its test id (the item's l10n
 * id). The item is chosen with the keyboard, as a person can: the menu opens below More, and in a
 * short window its last items lie below the bottom of the window, where no click reaches them.
 */
async function chooseFromMore(
    page: Page,
    dialog: Locator,
    moreLabel: string,
    itemTestId: string,
): Promise<void> {
    await dialog.getByText(moreLabel, { exact: true }).click();
    const item = page.getByRole("menuitem").and(page.getByTestId(itemTestId));
    await expect(item, `The More menu has no item ${itemTestId}.`).toBeVisible({
        timeout: 30000,
    });
    await item.press("Enter");
    await expect(item).toBeHidden({ timeout: 30000 });
}

/** Parse a timings file: one line per segment, start, end and text separated by tabs. */
function readTimingsFile(
    path: string,
): { start: number; end: number; text: string }[] {
    return fs
        .readFileSync(path, "utf8")
        .split(/\r?\n/)
        .filter((line) => line.trim() !== "")
        .map((line) => {
            const [start, end, text] = line.split("\t");
            return { start: Number(start), end: Number(end), text };
        });
}

test("Adjust Timings plays to its boundaries, and its timings file round-trips into the dialog and the playback [Test Case ID 519]", async ({
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

    const ui = await getUiStrings(page, {
        "CollectionTab.ContextMenu.More": "More",
        "Common.OK": "OK",
    });

    const bookFolder = await step(
        "Record a text box as a whole: By Whole Text Box, then Import Recording",
        async () => {
            const folder = await makeBookFromTemplate(page, "Basic Book");
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
            // Switches the tool to By Whole Text Box (in its Advanced section) before importing.
            await importNarration(page, folder, kGroup, "en", recording);
            return folder;
        },
    );
    if (!bookFolder) throw new Error("The book was not made.");

    const toolbox = page.frame({ name: "toolbox" })!;
    const dialog = page
        .getByRole("dialog")
        .filter({ has: page.locator("#waveform") });

    await step("Open Adjust Timings", async () => {
        await startNotingPlayback(page);
        const button = toolbox.locator(
            'button:has(img[src*="adjustTimings.svg"])',
        );
        await expect(
            button,
            "Adjust Timings... is disabled; it needs a text box recorded as a whole.",
        ).toBeEnabled({ timeout: 30000 });
        // The button itself ignores the pointer (pointer-events: none); its picture takes the click.
        await button.locator("img").click();
        await expect(
            dialog.locator("#waveform .playButton"),
            "The Adjust Timings dialog never showed one segment for each sentence.",
        ).toHaveCount(kSentences.length, { timeout: 60000 });
    });

    // Play the first segment to its end. That also gives the recording's length, which places the
    // segments the dialog shows.
    const duration = await step(
        "Play a segment, and see playback stop at its boundary",
        async () => {
            const before = await clickSegment(page, dialog, 0);
            const [play, pause] = await waitForPlayback(
                page,
                before,
                2,
                "Clicking the first segment did not play it to its end.",
            );
            expect(play.what).toBe("play");
            expect(pause.what).toBe("pause");
            const length = play.duration;
            expect(
                length,
                "the recording should be about as long as the copies of the sample in it",
            ).toBeGreaterThan(kCopiesOfSample * 0.5);
            const segments = await getShownSegments(page, length);
            expect(segments.map((segment) => segment.text)).toEqual(kSentences);
            expect(
                play.time,
                "the first segment should play from the start of the recording",
            ).toBeLessThan(0.05);
            expect(
                pause.time,
                `playback should stop where the first segment ends, ${segments[0].end} s`,
            ).toBeGreaterThanOrEqual(segments[0].end - 0.05);
            expect(pause.time).toBeLessThan(segments[0].end + kStopLateness);
            return length;
        },
    );
    if (!duration) throw new Error("The recording's length is unknown.");

    await step("Stop a segment while it plays, and play it again", async () => {
        const segments = await getShownSegments(page, duration);
        const before = await clickSegment(page, dialog, 1);
        const [play] = await waitForPlayback(
            page,
            before,
            1,
            "Clicking the second segment did not play it.",
        );
        expect(play.what).toBe("play");
        expect(
            Math.abs(play.time - segments[1].start),
            `the second segment should play from where it starts, ${segments[1].start} s`,
        ).toBeLessThan(0.05);
        // A click on the segment that is playing stops it. The click must land well before the
        // segment ends by itself; otherwise it would start the segment again rather than stop it.
        const position = await getPlayingPosition(page);
        expect(
            position,
            `the second segment should still be playing, well before its end at ${segments[1].end} s`,
        ).toEqual({ time: expect.any(Number), paused: false });
        expect(position.time).toBeLessThan(segments[1].end - 0.5);
        const beforeStop = await clickSegment(page, dialog, 1);
        const [stop] = await waitForPlayback(
            page,
            beforeStop,
            1,
            "Clicking the playing segment did not stop it.",
        );
        expect(stop.what).toBe("pause");
        expect(
            stop.time,
            "the click should have stopped the segment before its end",
        ).toBeLessThan(segments[1].end);

        const again = await clickSegment(page, dialog, 1);
        const [replay, end] = await waitForPlayback(
            page,
            again,
            2,
            "Clicking the stopped segment did not play it to its end.",
        );
        expect([replay.what, end.what]).toEqual(["play", "pause"]);
        expect(Math.abs(replay.time - segments[1].start)).toBeLessThan(0.05);
        expect(
            end.time,
            `playback should stop where the second segment ends, ${segments[1].end} s`,
        ).toBeGreaterThanOrEqual(segments[1].end - 0.05);
        expect(end.time).toBeLessThan(segments[1].end + kStopLateness);
    });

    const dragged = await step(
        "Drag the boundary between the first two segments, and see playback follow it",
        async () => {
            const segments = await getShownSegments(page, duration);
            const handle = dialog
                .locator('#waveform [part~="region-handle-right"]')
                .first();
            const box = (await handle.boundingBox())!;
            const x = box.x + box.width / 2;
            const y = box.y + box.height / 2;
            const before = (await getPlayback(page)).length;
            await page.mouse.move(x, y);
            await page.mouse.down();
            // Later in the recording, by about half a second at the dialog's scale.
            for (let step = 1; step <= 10; step++)
                await page.mouse.move(x + step * 6, y);
            await page.mouse.up();

            await expect
                .poll(
                    async () =>
                        (await getShownSegments(page, duration))[0].end -
                        segments[0].end,
                    {
                        timeout: 15000,
                        message: "Dragging the boundary did not move it.",
                    },
                )
                .toBeGreaterThan(0.2);
            const moved = await getShownSegments(page, duration);
            expect(
                Math.abs(moved[1].start - moved[0].end),
                "the second segment should start where the first now ends",
            ).toBeLessThan(0.01);
            expect(
                moved[1].end,
                "the second segment's other boundary should not have moved",
            ).toBeCloseTo(segments[1].end, 2);

            // After a drag the dialog stops whatever was playing, then plays the start of the
            // segment after the boundary.
            await expect
                .poll(
                    async () =>
                        (await getPlayback(page))
                            .slice(before)
                            .map((event) => event.what)
                            .join(" "),
                    {
                        timeout: 30000,
                        message:
                            "The dialog did not play, and then stop, the segment after the dragged boundary.",
                    },
                )
                .toMatch(/play pause$/);
            const afterDrag = (await getPlayback(page)).slice(before);
            const play = afterDrag.find((event) => event.what === "play")!;
            expect(
                Math.abs(play.time - moved[1].start),
                `playback after the drag should start at the new boundary, ${moved[1].start} s`,
            ).toBeLessThan(0.05);

            const again = await clickSegment(page, dialog, 0);
            const [first, end] = await waitForPlayback(
                page,
                again,
                2,
                "Clicking the first segment did not play it to its end.",
            );
            expect([first.what, end.what]).toEqual(["play", "pause"]);
            expect(
                end.time,
                `the first segment should now stop at the moved boundary, ${moved[0].end} s`,
            ).toBeGreaterThanOrEqual(moved[0].end - 0.05);
            expect(end.time).toBeLessThan(moved[0].end + kStopLateness);
            return moved;
        },
    );
    if (!dragged) throw new Error("The boundary was not dragged.");

    const timingsFile = await step(
        "Edit Timings File... writes the dialog's boundaries; edit the file and save it",
        async () => {
            const opened: string[] = [];
            // Bloom would open the file in the program the machine opens .txt files with. The
            // test edits the file itself, so it answers that request instead of letting Bloom
            // start an editor.
            await page.route(/\/fileIO\/openFileInDefaultEditor/, (route) => {
                // The dialog sends the path as it is, not URL-encoded.
                opened.push(
                    JSON.parse(route.request().postData() ?? "{}").path,
                );
                return route.fulfill({ status: 200, body: "" });
            });
            await chooseFromMore(
                page,
                dialog,
                ui["CollectionTab.ContextMenu.More"],
                "EditTab.Toolbox.TalkingBookTool.EditTimingsFile",
            );
            const message = page
                .getByRole("dialog")
                .filter({ hasText: "_timings.txt" });
            await expect(
                message,
                "Edit Timings File... did not say which file to edit.",
            ).toBeVisible({ timeout: 30000 });
            const messageText = (await message.textContent()) ?? "";
            await message
                .getByRole("button", { name: ui["Common.OK"], exact: true })
                .click();
            await expect
                .poll(() => opened.length, {
                    timeout: 30000,
                    message:
                        "OK on the message did not ask Bloom to open the timings file.",
                })
                .toBe(1);
            // The timings file is named for the text box, whose id also names its recording.
            const boxId = await editablePageFrame(page)
                .locator(`${kGroup} .bloom-editable[lang="en"]`)
                .first()
                .getAttribute("id");
            expect(boxId, "the text box should have an id").toBeTruthy();
            const expectedFile = Path.join(
                bookFolder,
                "audio",
                `${boxId}_timings.txt`,
            );
            const file = opened[0];
            expect(
                Path.resolve(file).toLowerCase(),
                "OK should open the text box's own timings file",
            ).toBe(Path.resolve(expectedFile).toLowerCase());
            expect(
                messageText,
                "the message should name the file that OK opens",
            ).toContain(file);

            const written = readTimingsFile(file);
            expect(
                written.map((line) => line.text),
                "the timings file should have a line for each segment",
            ).toEqual(kSentences);
            written.forEach((line, index) => {
                expect(
                    line.start,
                    `line ${index + 1} of the timings file should start where the dialog showed`,
                ).toBeCloseTo(dragged[index].start, 2);
                if (index < written.length - 1)
                    expect(
                        line.end,
                        `line ${index + 1} of the timings file should end at the dialog's boundary`,
                    ).toBeCloseTo(dragged[index].end, 2);
            });
            // The last segment has no boundary to drag; it ends with the recording.
            expect(
                written[2].end,
                `the last line should end after it starts, and no later than the recording, ${duration} s`,
            ).toBeGreaterThan(written[2].start);
            expect(written[2].end).toBeLessThanOrEqual(duration + 0.05);

            // Move both boundaries, as a person would in an editor.
            const firstEnd = Number((written[0].end - 0.6).toFixed(3));
            const secondEnd = Number((written[1].end + 0.4).toFixed(3));
            const lastEnd = written[2].end;
            expect(firstEnd).toBeGreaterThan(0.3);
            expect(secondEnd).toBeLessThan(lastEnd - 0.3);
            const edited = [
                [0, firstEnd, kSentences[0]],
                [firstEnd, secondEnd, kSentences[1]],
                [secondEnd, lastEnd, kSentences[2]],
            ];
            fs.writeFileSync(
                file,
                edited
                    .map(
                        ([start, end, text]) =>
                            `${Number(start).toFixed(3)}\t${Number(end).toFixed(3)}\t${text}\r\n`,
                    )
                    .join(""),
            );
            return { path: file, ends: [firstEnd, secondEnd], lastEnd };
        },
    );
    if (!timingsFile) throw new Error("The timings file was not edited.");

    await step(
        "Apply Timings File... starts at the edited file, and the dialog shows its times",
        async () => {
            const chooserRequests: Request[] = [];
            const noteChooser = (request: Request) => {
                if (/\/fileIO\/chooseFile/.test(request.url()))
                    chooserRequests.push(request);
            };
            page.on("request", noteChooser);
            await armFileChooser(page, timingsFile.path);
            await chooseFromMore(
                page,
                dialog,
                ui["CollectionTab.ContextMenu.More"],
                "EditTab.Toolbox.TalkingBookTool.ApplyTimingsFile",
            );
            await expect
                .poll(() => chooserRequests.length, {
                    timeout: 30000,
                    message:
                        "Apply Timings File... did not open a file chooser.",
                })
                .toBe(1);
            page.off("request", noteChooser);
            const asked = JSON.parse(chooserRequests[0].postData() ?? "{}");
            expect(
                asked.defaultPath,
                "the file chooser should start at the timings file just edited",
            ).toBe(timingsFile.path);

            await expect
                .poll(
                    async () =>
                        (await getShownSegments(page, duration))
                            .slice(0, 2)
                            .map((segment) => Number(segment.end.toFixed(2))),
                    {
                        timeout: 30000,
                        message:
                            "The dialog's boundaries never moved to the times in the edited file.",
                    },
                )
                .toEqual(timingsFile.ends.map((end) => Number(end.toFixed(2))));
            const shown = await getShownSegments(page, duration);
            expect(shown[1].start).toBeCloseTo(timingsFile.ends[0], 2);
            expect(shown[2].start).toBeCloseTo(timingsFile.ends[1], 2);
        },
    );

    await step(
        "OK, then Check: each segment is highlighted from the time the file gave it",
        async () => {
            await dialog.getByTestId("dialog-ok").click();
            await expect(dialog).toBeHidden({ timeout: 30000 });
            const box = editablePageFrame(page).locator(
                ".bloom-editable.bloom-postAudioSplit",
            );
            await expect(
                box,
                "OK in Adjust Timings did not mark the text box as split into timed segments.",
            ).toHaveCount(1, { timeout: 30000 });
            const saved = (
                (await box.getAttribute("data-audiorecordingendtimes")) ?? ""
            )
                .split(" ")
                .map(Number);
            expect(
                saved,
                "OK should keep all three end times applied from the file",
            ).toEqual([...timingsFile.ends, timingsFile.lastEnd]);

            const pageFrame = editablePageFrame(page);
            await startNotingHighlights(pageFrame);
            await toolbox
                .locator(
                    'button:has(img[src*="play_enabled.svg"], img[src*="play_expected.svg"]) img',
                )
                .click();
            // The player can still say "ended" from an earlier playback, so the last sentence
            // having been highlighted is part of what this waits for.
            await expect
                .poll(
                    async () => {
                        const noted = await getHighlights(pageFrame);
                        return (
                            noted.ended &&
                            noted.changes.some(
                                (change) => change.text === kSentences[2],
                            )
                        );
                    },
                    {
                        timeout: 60000,
                        message:
                            "Check never played the recording to its last sentence and its end.",
                    },
                )
                .toBe(true);
            const { changes } = await getHighlights(pageFrame);
            // Each sentence's first highlight, in the order the tool gave them.
            const firsts = kSentences.map((sentence) =>
                changes.find((change) => change.text === sentence),
            );
            firsts.forEach((change, index) =>
                expect(
                    change,
                    `Check should highlight "${kSentences[index]}" on its own. Highlights: ${JSON.stringify(changes)}`,
                ).toBeDefined(),
            );
            const starts = [0, ...timingsFile.ends];
            firsts.forEach((change, index) => {
                expect(
                    change!.time,
                    `"${kSentences[index]}" should not be highlighted before ${starts[index]} s, where the file starts it`,
                ).toBeGreaterThanOrEqual(starts[index] - 0.05);
                expect(
                    change!.time,
                    `"${kSentences[index]}" should be highlighted soon after ${starts[index]} s, where the file starts it`,
                ).toBeLessThan(starts[index] + 0.5);
            });
            expect(
                changes.indexOf(firsts[0]!) < changes.indexOf(firsts[1]!) &&
                    changes.indexOf(firsts[1]!) < changes.indexOf(firsts[2]!),
                "the sentences should be highlighted in order",
            ).toBe(true);
        },
    );
});
