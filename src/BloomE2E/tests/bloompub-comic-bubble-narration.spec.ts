// Recordings of comic speech bubbles play in the published book: a page whose only text is a
// recorded speech bubble plays that recording, and a page with a recorded speech bubble and a
// recorded text box plays both, each under its own text (BL-7930 once left a bubble's recording
// unplayed).
//
// Automates the manual test "Record Comic Audio Playback" (Test Case ID 321), with the steps of its
// older form (bloom-test-cases TC415): a "Just an Image" page with a speech bubble saying "Hello!",
// then a "Basic Text & Image" page with a speech bubble saying "This is a car image!" and the text
// "This is a car", each recorded in the Talking Book tool, then published as a BloomPUB.
//
// The case reads the book in Bloom Reader on a phone. Bloom Reader shows a BloomPUB with Bloom
// Player, which is also what the BloomPUB screen's preview runs, so the test plays the book there;
// and it checks that the .bloompub file Bloom writes, which is what goes to the phone, carries each
// recording under its sentence. The phone, the USB transfer and Bloom Reader's book list are left
// unchecked. Recording needs a microphone, which a test has not got: the test puts a recording
// under each id the Talking Book tool gave a sentence, where Bloom's own recording would go, so the
// case's "Check" of each recording is not repeated here. Each sentence gets a recording of its own
// length, and the test checks that each sentence's own file is the one that plays, to its end,
// while Bloom Player highlights that sentence.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Frame, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { goToPreviewPage } from "../helpers/bloomPlayer";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    makeAllBloomPubs,
    openMakeAllBloomPubsDialog,
    readZip,
    setMakeAllBloomPubsOption,
} from "../helpers/bulkBloomPub";
import {
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import {
    kEnterpriseSubscriptionCode,
    kTestBookshelves,
} from "../helpers/collectionSettings";
import { chooseImageFile } from "../helpers/images";
import {
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    narrationFilePath,
    sampleNarrationFile,
    waitForSentencesMarked,
} from "../helpers/talkingBook";
import { enableToolForBook, expandToolByHeader } from "../helpers/toolbox";
import { setUiStateViaApi } from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "bloompub-comic-bubble-narration",
        languages: ["en"],
        // The case grants a subscription before it starts; speech bubbles and publishing a
        // talking book are done under one.
        subscriptionCode: kEnterpriseSubscriptionCode,
        // With no bookshelf, Make All BloomPUBs fails before it makes anything (BL-17197).
        bookshelf: kTestBookshelves[0],
    },
});

const L1 = "en";
const IMAGES = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
);

/** The picture box of an ordinary page, which a speech bubble is put on. */
const PICTURE_BOX = ".bloom-canvas";
/** The text box of a speech bubble. */
const BUBBLE_GROUP = ".bloom-canvas-element .bloom-translationGroup";
/** The page's own text box: not a bubble's, and not a picture's description. */
const TEXT_GROUP =
    ".bloom-translationGroup:not(.bloom-canvas-element *):not(.bloom-imageDescription)";

const FIRST_BUBBLE = "Hello!";
const SECOND_BUBBLE = "This is a car image!";
const PAGE_TEXT = "This is a car";

/** One recorded sentence: the id that names its recording, and its text. */
interface IRecording {
    id: string;
    text: string;
    /** What the test put in the sentence's recording file. */
    content: Buffer;
}

/**
 * Give the sentence `id` a recording made of `copies` copies of the sample recording, so that no
 * two sentences' recordings are alike.
 */
function recordSentence(
    bookFolder: string,
    id: string,
    text: string,
    copies: number,
): IRecording {
    const file = narrationFilePath(bookFolder, id);
    fs.mkdirSync(Path.dirname(file), { recursive: true });
    const content = Buffer.concat(
        Array(copies).fill(fs.readFileSync(sampleNarrationFile)),
    );
    fs.writeFileSync(file, content);
    return { id, text, content };
}

/**
 * One thing that happened in Bloom Player: one of its media elements started a file ("play"),
 * played it to its end ("ended") or failed to load it ("error"), or what it highlights changed
 * ("highlight").
 */
interface IPlaybackEvent {
    event: string;
    /** The file name of what the media element was playing; empty for "highlight". */
    name: string;
    /** The text of each element Bloom Player was highlighting as read, at that moment. */
    highlighted: string[];
}

/**
 * Start noting, in Bloom Player's document, each IPlaybackEvent. Called again, it empties the
 * record.
 */
async function startNotingPlayback(player: Frame): Promise<void> {
    await player.evaluate(() => {
        const w = window as unknown as { e2ePlayback?: unknown[] };
        if (w.e2ePlayback) {
            w.e2ePlayback.length = 0;
            return;
        }
        const events: unknown[] = [];
        w.e2ePlayback = events;
        // Bloom Player marks the sentence it is reading ui-audioCurrent (bloom-player's
        // narration.ts), on the page showing.
        const highlighted = () =>
            Array.from(
                document.querySelectorAll(
                    ".swiper-slide-active .ui-audioCurrent",
                ),
            ).map((e) => (e.textContent ?? "").trim());
        let lastHighlight = "";
        new MutationObserver(() => {
            const now = highlighted();
            if (JSON.stringify(now) === lastHighlight) return;
            lastHighlight = JSON.stringify(now);
            events.push({ event: "highlight", name: "", highlighted: now });
        }).observe(document.body, {
            attributes: true,
            attributeFilter: ["class"],
            subtree: true,
        });
        // A media element's events do not bubble, so they are caught on their way down, which is
        // also before Bloom Player's own handlers move the highlight on.
        for (const type of ["play", "ended", "error"])
            document.addEventListener(
                type,
                (e) => {
                    const media = e.target;
                    if (!(media instanceof HTMLMediaElement)) return;
                    events.push({
                        event: type,
                        name: decodeURIComponent(
                            (media.currentSrc || media.src || "")
                                .split("?")[0]
                                .split("/")
                                .pop() ?? "",
                        ),
                        highlighted: highlighted(),
                    });
                },
                true,
            );
    });
}

async function getPlayback(player: Frame): Promise<IPlaybackEvent[]> {
    return player.evaluate(
        () =>
            (window as unknown as { e2ePlayback: IPlaybackEvent[] })
                .e2ePlayback,
    );
}

/**
 * Whether Bloom Player has finished reading the page: every one of `expected` has ended, and since
 * the last of them Bloom Player has taken its highlight off the page, as it does when the page's
 * narration is over rather than moving it to another sentence.
 */
function pageFinished(events: IPlaybackEvent[], expected: string[]): boolean {
    const lastEnd = Math.max(
        ...expected.map((name) =>
            events.findIndex((e) => e.event === "ended" && e.name === name),
        ),
    );
    if (
        expected.some(
            (name) =>
                !events.some((e) => e.event === "ended" && e.name === name),
        )
    )
        return false;
    return events
        .slice(lastEnd + 1)
        .some((e) => e.event === "highlight" && e.highlighted.length === 0);
}

/**
 * Turn the preview to `pageId` and check that Bloom Player, as it does for a reader, plays each of
 * the page's `recordings` once, to its end, while highlighting that recording's sentence, and
 * plays nothing else before it finishes the page.
 */
async function expectPageToPlayItsRecordings(
    page: Page,
    player: Frame,
    pageId: string,
    recordings: IRecording[],
    what: string,
): Promise<void> {
    const expected = recordings.map((r) => `${r.id}.mp3`);
    await startNotingPlayback(player);
    await goToPreviewPage(page, player, pageId);
    await expect
        .poll(
            async () => {
                const events = await getPlayback(player);
                // A file that fails to load can never end; stop waiting and let the checks below
                // say which.
                if (events.some((e) => e.event === "error")) return "error";
                return pageFinished(events, expected) ? "finished" : "waiting";
            },
            {
                timeout: 60000,
                message: `Bloom Player never finished reading ${what}, playing each of its recordings to its end.`,
            },
        )
        .not.toBe("waiting");
    const events = await getPlayback(player);
    expect(
        events.filter((e) => e.event === "error").map((e) => e.name),
        `no recording of ${what} should fail to load`,
    ).toEqual([]);
    expect(
        events
            .filter((e) => e.event === "play")
            .map((e) => e.name)
            .sort(),
        `${what} should play each of its recordings once, and nothing else`,
    ).toEqual([...expected].sort());
    expect(
        events
            .filter((e) => e.event === "ended")
            .map((e) => e.name)
            .sort(),
        `each recording of ${what} should play to its end once`,
    ).toEqual([...expected].sort());
    for (const recording of recordings)
        expect(
            events
                .filter(
                    (e) =>
                        (e.event === "play" || e.event === "ended") &&
                        e.name === `${recording.id}.mp3`,
                )
                .map((e) => `${e.event}: ${e.highlighted.join(" | ")}`),
            `"${recording.text}" should be the sentence highlighted while its recording starts and ends`,
        ).toEqual([`play: ${recording.text}`, `ended: ${recording.text}`]);
}

test("recorded speech bubbles, and a recorded text box beside one, all play in the BloomPUB [Test Case ID 321]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(600000);
    // The Make All BloomPUBs helpers find the dialog's controls by their English labels.
    await setUiStateViaApi(bloomApp, "en", false);
    const page = bloomApp.page;

    const book = await step(
        `Make a book with a "Just an Image" page and a "Basic Text & Image" page`,
        async () => {
            const bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just an Image");
            const [imagePage] = await getContentPages(page);
            await goToPage(page, imagePage.id);
            await addPage(page, "Basic Text & Image");
            // The case turns on the Canvas tool (once called the Overlay tool) in the toolbox's
            // More... menu.
            await enableToolForBook(page, bookFolder, "canvas");
            const pages = await getContentPages(page);
            expect(
                pages.length,
                "test setup: the book should have two content pages",
            ).toBe(2);
            expect(
                pages[0].id,
                'test setup: the "Basic Text & Image" page should come after the "Just an Image" page',
            ).toBe(imagePage.id);
            return {
                bookFolder,
                imagePageId: imagePage.id,
                textPageId: pages[1].id,
            };
        },
    );
    if (!book) throw new Error("The book was not made.");

    const firstPage = await step(
        `On the "Just an Image" page, choose a picture, add a speech bubble saying "${FIRST_BUBBLE}", and record it`,
        async () => {
            await goToPage(page, book.imagePageId);
            await chooseImageFile(page, Path.join(IMAGES, "bird.png"));
            const bubble = await dragPaletteItemOntoCanvas(
                page,
                "speech",
                { xFraction: 0.3, yFraction: 0.3 },
                PICTURE_BOX,
            );
            await selectCanvasElement(page, bubble);
            await typeInCanvasElement(page, FIRST_BUBBLE);
            await expandToolByHeader(page, "talkingBook");
            const [id] = await waitForSentencesMarked(page, BUBBLE_GROUP, L1, [
                FIRST_BUBBLE,
            ]);
            return [recordSentence(book.bookFolder, id, FIRST_BUBBLE, 1)];
        },
    );
    if (!firstPage) throw new Error("The first bubble was not recorded.");

    const secondPage = await step(
        `On the "Basic Text & Image" page, choose a picture, add a speech bubble saying "${SECOND_BUBBLE}", type "${PAGE_TEXT}" in the text box, and record both`,
        async () => {
            await goToPage(page, book.textPageId);
            await chooseImageFile(
                page,
                Path.join(IMAGES, "house-landscape.png"),
            );
            const bubble = await dragPaletteItemOntoCanvas(
                page,
                "speech",
                { xFraction: 0.3, yFraction: 0.3 },
                PICTURE_BOX,
            );
            await selectCanvasElement(page, bubble);
            await typeInCanvasElement(page, SECOND_BUBBLE);
            await typeInGroup(page, TEXT_GROUP, L1, PAGE_TEXT);
            await expandToolByHeader(page, "talkingBook");
            const [bubbleId] = await waitForSentencesMarked(
                page,
                BUBBLE_GROUP,
                L1,
                [SECOND_BUBBLE],
            );
            const [textId] = await waitForSentencesMarked(
                page,
                TEXT_GROUP,
                L1,
                [PAGE_TEXT],
            );
            return [
                recordSentence(book.bookFolder, bubbleId, SECOND_BUBBLE, 2),
                recordSentence(book.bookFolder, textId, PAGE_TEXT, 3),
            ];
        },
    );
    if (!secondPage) throw new Error("The second page was not recorded.");

    const player = await step(
        "Publish the book as a BloomPUB and show it in the preview",
        async () => {
            // Leaving the Edit tab saves the book.
            await switchTab(page, "collection");
            await openPublishDestination(page, "BloomPUB");
            return showBloomPubPreview(page);
        },
    );
    if (!player) throw new Error("The BloomPUB preview never opened.");

    await step(
        `Turn to the "Just an Image" page and check that the bubble's recording plays`,
        async () => {
            await expectPageToPlayItsRecordings(
                page,
                player,
                book.imagePageId,
                firstPage,
                'the "Just an Image" page',
            );
        },
    );

    await step(
        `Turn to the "Basic Text & Image" page and check that the bubble's and the text's recordings both play`,
        async () => {
            await expectPageToPlayItsRecordings(
                page,
                player,
                book.textPageId,
                secondPage,
                'the "Basic Text & Image" page',
            );
        },
    );

    await step(
        "Make the .bloompub file and check that it carries each recording under its sentence",
        async () => {
            // A real .bloompub, as Bloom writes it to send to a phone. Make All BloomPUBs writes it
            // without the save dialog that the single-book Save opens.
            await openMakeAllBloomPubsDialog(page);
            await setMakeAllBloomPubsOption(page, "bookshelfFile", false);
            await setMakeAllBloomPubsOption(page, "bloomBundle", false);
            const result = await makeAllBloomPubs(page);
            const bloomPubName = `${Path.basename(book.bookFolder)}.bloompub`;
            const bloomPub = result.files.get(bloomPubName);
            if (!bloomPub)
                throw new Error(
                    `Make All BloomPUBs made no ${bloomPubName}. It made: ` +
                        `${[...result.files.keys()].join(", ")}. The progress box said:\n${result.log}`,
                );
            const entries = readZip(bloomPub);
            const htmlEntries = entries.filter((e) => e.name.endsWith(".htm"));
            expect(
                htmlEntries.map((e) => e.name),
                "the BloomPUB should hold one .htm",
            ).toHaveLength(1);
            const html = htmlEntries[0].content.toString("utf8");
            for (const recording of [...firstPage, ...secondPage]) {
                const audio = entries.find(
                    (e) => e.name === `audio/${recording.id}.mp3`,
                );
                expect(
                    audio?.content.equals(recording.content),
                    `the BloomPUB should carry "${recording.text}"'s recording, audio/${recording.id}.mp3, as it was recorded`,
                ).toBe(true);
            }
            const sentences = await player.evaluate((source) => {
                const dom = new DOMParser().parseFromString(
                    source,
                    "text/html",
                );
                return Array.from(dom.querySelectorAll(".audio-sentence[id]"))
                    .map((e) => `${e.id}: ${(e.textContent ?? "").trim()}`)
                    .sort();
            }, html);
            expect(
                sentences,
                "the BloomPUB's book should mark each recorded sentence, and only those, with the id of its recording",
            ).toEqual(
                [...firstPage, ...secondPage]
                    .map((r) => `${r.id}: ${r.text}`)
                    .sort(),
            );
        },
    );
});
