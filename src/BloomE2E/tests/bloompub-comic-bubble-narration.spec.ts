// Recordings of comic speech bubbles play in the published book: a page whose only text is a
// recorded speech bubble plays that recording, and a page with a recorded speech bubble and a
// recorded text box plays both, each its own (BL-7930 once left a bubble's recording unplayed).
//
// Automates the manual test "Record Comic Audio Playback" (Test Case ID 321), with the steps of its
// older form (bloom-test-cases TC415): a "Just an Image" page with a speech bubble saying "Hello!",
// then a "Basic Text & Image" page with a speech bubble saying "This is a car image!" and the text
// "This is a car", each recorded in the Talking Book tool, then published as a BloomPUB.
//
// The case reads the book in Bloom Reader on a phone. Bloom Reader shows a BloomPUB with Bloom
// Player, which is also what the BloomPUB screen's preview runs, so the test reads the book there
// and leaves the phone, the USB transfer and Bloom Reader's book list unchecked. Recording needs a
// microphone, which a test has not got: the test puts a recording under each id the Talking Book
// tool gave a sentence, where Bloom's own recording would go, so the case's "Check" of each
// recording is not repeated here. Each sentence gets a recording of its own length, and the test
// checks that each sentence's own file is the one that plays, to its end.

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
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
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
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "bloompub-comic-bubble-narration",
        languages: ["en"],
        // The case grants a subscription before it starts; speech bubbles and publishing a
        // talking book are done under one.
        subscriptionCode: kEnterpriseSubscriptionCode,
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

/**
 * Give the sentence `id` a recording made of `copies` copies of the sample recording, so that no
 * two sentences' recordings are alike.
 */
function recordSentence(bookFolder: string, id: string, copies: number): void {
    const file = narrationFilePath(bookFolder, id);
    fs.mkdirSync(Path.dirname(file), { recursive: true });
    const sample = fs.readFileSync(sampleNarrationFile);
    fs.writeFileSync(file, Buffer.concat(Array(copies).fill(sample)));
}

/** One thing that happened to an audio or video element in Bloom Player. */
interface IMediaEvent {
    /** "play", "ended" or "error". */
    event: string;
    /** The file name of what the element was playing. */
    name: string;
}

/**
 * Start noting, in Bloom Player's document, each time one of its media elements starts playing a
 * file, plays a file to its end, or fails to load one. Called again, it empties the record.
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
        // A media element's events do not bubble, so they are caught on their way down.
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
                    });
                },
                true,
            );
    });
}

async function getPlayback(player: Frame): Promise<IMediaEvent[]> {
    return player.evaluate(
        () => (window as unknown as { e2ePlayback: IMediaEvent[] }).e2ePlayback,
    );
}

/**
 * Turn the preview to `pageId` and check that Bloom Player, as it does for a reader, plays each of
 * the page's recordings, `ids`, to its end, and nothing else.
 */
async function expectPageToPlayItsRecordings(
    page: Page,
    player: Frame,
    pageId: string,
    ids: string[],
    what: string,
): Promise<void> {
    const expected = ids.map((id) => `${id}.mp3`).sort();
    await startNotingPlayback(player);
    await goToPreviewPage(page, player, pageId);
    await expect
        .poll(
            async () => {
                const events = await getPlayback(player);
                // A file that fails to load can never end; stop waiting and let the checks below
                // say which.
                if (events.some((e) => e.event === "error")) return "error";
                return expected.every((name) =>
                    events.some((e) => e.event === "ended" && e.name === name),
                )
                    ? "all ended"
                    : "waiting";
            },
            {
                timeout: 60000,
                message: `Bloom Player never played every recording of ${what} to its end.`,
            },
        )
        .not.toBe("waiting");
    const events = await getPlayback(player);
    expect(
        events.filter((e) => e.event === "error").map((e) => e.name),
        `no recording of ${what} should fail to load`,
    ).toEqual([]);
    expect(
        [
            ...new Set(
                events.filter((e) => e.event === "play").map((e) => e.name),
            ),
        ].sort(),
        `${what} should play its recordings and nothing else`,
    ).toEqual(expected);
    expect(
        events
            .filter((e) => e.event === "ended")
            .map((e) => e.name)
            .sort(),
        `each recording of ${what} should play to its end once`,
    ).toEqual(expected);
}

test("recorded speech bubbles, and a recorded text box beside one, all play in the BloomPUB [Test Case ID 321]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);

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
            const ids = await waitForSentencesMarked(page, BUBBLE_GROUP, L1, [
                FIRST_BUBBLE,
            ]);
            recordSentence(book.bookFolder, ids[0], 1);
            return ids;
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
            const bubbleIds = await waitForSentencesMarked(
                page,
                BUBBLE_GROUP,
                L1,
                [SECOND_BUBBLE],
            );
            const textIds = await waitForSentencesMarked(page, TEXT_GROUP, L1, [
                PAGE_TEXT,
            ]);
            recordSentence(book.bookFolder, bubbleIds[0], 2);
            recordSentence(book.bookFolder, textIds[0], 3);
            return [...bubbleIds, ...textIds];
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
});
