// The order in which a Canvas page's text boxes and comic bubbles are narrated, set with the
// Talking Book tool's playback order buttons, is the order in which the page plays: in the Edit
// tab's "Listen to the whole page", in the BloomPUB that bloom-player reads, and in the ePUB's
// media overlay.
//
// Automates "Playback Ordering in Canvas" (Test Case ID 487).
//
// Recording needs a microphone (AUTOMATION-DEBT.md), so each sentence gets the suite's sample mp3
// under the name Bloom gives it (addNarration in helpers/talkingBook.ts), which is what a recording
// leaves behind. Which sentence Listen played is read from the file each play() starts: Bloom
// names a narration file after the id of the sentence it narrates.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Frame, Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    canvasElement,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    typeInCanvasElement,
    type PaletteItem,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    getStagedNarrationIds,
    openPublishDestination,
    selectPublishDestination,
    stageBloomPub,
} from "../helpers/publish";
import {
    addNarration,
    getNarrationSentences,
    openAdvancedSection,
} from "../helpers/talkingBook";
import { expandToolByHeader, toolboxFrame } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "talking-book-playback-order",
        languages: ["en"],
        // Below Pro, Bloom will not publish a book whose author put a canvas element in it.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The page's canvas elements, in the order they are added, each with its one sentence. */
const kElements: { item: PaletteItem; text: string; at: [number, number] }[] = [
    { item: "none", text: "Alpha text block.", at: [0.25, 0.25] },
    { item: "speech", text: "Bravo speech bubble.", at: [0.75, 0.25] },
    { item: "caption", text: "Charlie caption.", at: [0.25, 0.75] },
    { item: "speech", text: "Delta speech bubble.", at: [0.75, 0.75] },
];
const [ALPHA, BRAVO, CHARLIE, DELTA] = kElements.map((e) => e.text);

/**
 * The order the buttons set: Alpha's "+" moves it after Bravo, and then Delta's "-" moves it
 * before Charlie.
 */
const kChosenOrder = [BRAVO, ALPHA, DELTA, CHARLIE];

/**
 * The index (see canvasElement) of each of kElements' canvas elements, once added. A new Canvas
 * page already holds one canvas element, its background picture, so these do not start at 0.
 */
const elementIndexes: number[] = [];

/** The playback order controls the tool shows on kElements[`which`]. */
function playbackOrderControls(page: Page, which: number): Locator {
    return canvasElement(page, elementIndexes[which]).locator(
        ".bloom-playbackOrderControlsContainer",
    );
}

/** The number each canvas element's playback order controls show, in the order of kElements. */
async function getShownPlaybackOrder(page: Page): Promise<string[]> {
    const numbers: string[] = [];
    for (let i = 0; i < kElements.length; i++)
        numbers.push(
            (
                await playbackOrderControls(page, i)
                    .locator(".playbackOrderNumber")
                    .innerText()
            ).trim(),
        );
    return numbers;
}

/** Click the "Show playback order buttons" switch and wait for it to be `on`. */
async function setShowPlaybackOrder(page: Page, on: boolean): Promise<void> {
    await openAdvancedSection(page);
    const input = toolboxFrame(page).locator(
        '[data-testid="show-playback-order-switch"] input',
    );
    await expect(input).toBeChecked({ checked: !on, timeout: 30000 });
    await input.click();
    await expect(input).toBeChecked({ checked: on, timeout: 30000 });
}

/**
 * Have `frame` note the file of every audio or video that plays to its end, so that a test can
 * read back which narration was heard, in what order (see getFinishedFiles). A file that fails to
 * load never ends, so it is not noted. Notes nothing that finished before this was called.
 */
async function recordFinishedPlays(frame: Frame): Promise<void> {
    await frame.evaluate(() => {
        const w = window as unknown as {
            e2eFinishedFiles?: string[];
            e2eListeningForEnded?: boolean;
        };
        w.e2eFinishedFiles = [];
        if (w.e2eListeningForEnded) return;
        w.e2eListeningForEnded = true;
        // "ended" does not bubble, so listen while it is on its way down to the media element.
        document.addEventListener(
            "ended",
            (event) => {
                const media = event.target as HTMLMediaElement;
                w.e2eFinishedFiles?.push(media.currentSrc || media.src);
            },
            true,
        );
    });
}

/**
 * The base names of the files that media in `frame` played to the end since recordFinishedPlays,
 * in the order they finished.
 */
async function getFinishedFiles(frame: Frame): Promise<string[]> {
    const urls = await frame.evaluate(
        () =>
            (window as unknown as { e2eFinishedFiles?: string[] })
                .e2eFinishedFiles ?? [],
    );
    return urls.map((url) => {
        // The Edit tab plays a narration through Bloom's audio/wavFile API, which names the file
        // in its `id` parameter.
        const parsed = new URL(url);
        const path = decodeURIComponent(
            parsed.searchParams.get("id") ?? parsed.pathname,
        );
        return Path.basename(path, Path.extname(path));
    });
}

/** The text of the sentences narrated by these files, which are named by sentence id. */
function textsOf(fileNames: string[], textById: Map<string, string>): string[] {
    return fileNames.map(
        (name) => textById.get(name) ?? `(no sentence ${name})`,
    );
}

/**
 * Wait until media in the frames have played `count` narration files to their end, and return
 * the sentences they narrate, in the order they were heard.
 */
async function waitForPlayedSentences(
    frames: Frame[],
    textById: Map<string, string>,
    count: number,
    where: string,
): Promise<string[]> {
    let played: string[] = [];
    try {
        await expect
            .poll(
                async () => {
                    played = [];
                    for (const frame of frames)
                        played.push(...(await getFinishedFiles(frame)));
                    return played.length;
                },
                { timeout: 120000 },
            )
            .toBeGreaterThanOrEqual(count);
    } catch {
        throw new Error(
            `${where} did not play all ${count} narrations; it played ` +
                `${JSON.stringify(textsOf(played, textById))}.`,
        );
    }
    return textsOf(played, textById);
}

/**
 * The sentence ids on page `pageId` of a staged BloomPUB, in the order bloom-player narrates
 * them: by the tabindex of the text box each is in, as Talking Book's playback order buttons set
 * it (sortByTabindex in audioRecording.ts; bloom-player sorts the same way). The BloomPUB preview
 * is not used for this: under automation bloom-player shows no play button on this page and
 * starts no narration.
 */
async function getStagedPlaybackOrder(
    page: Page,
    stagedFolder: string,
    pageId: string,
): Promise<string[]> {
    const htmFile = fs
        .readdirSync(stagedFolder)
        .find((name) => name.toLowerCase().endsWith(".htm"));
    if (!htmFile)
        throw new Error(
            `The staged BloomPUB in ${stagedFolder} has no .htm file.`,
        );
    const html = fs.readFileSync(Path.join(stagedFolder, htmFile), "utf8");
    return page.evaluate(
        ({ html, pageId }) => {
            const dom = new DOMParser().parseFromString(html, "text/html");
            const bookPage = dom.querySelector(`.bloom-page[id="${pageId}"]`);
            if (!bookPage) return [`(no page ${pageId})`];
            const groups = Array.from(
                bookPage.querySelectorAll(".bloom-translationGroup"),
            ).filter((group) => group.querySelector(".audio-sentence[id]"));
            const order = (group: Element) =>
                Number(group.getAttribute("tabindex") ?? "999");
            return groups
                .map((group, index) => ({ group, index }))
                .sort(
                    (a, b) =>
                        order(a.group) - order(b.group) || a.index - b.index,
                )
                .flatMap(({ group }) =>
                    Array.from(
                        group.querySelectorAll(".audio-sentence[id]"),
                    ).map((sentence) => sentence.id),
                );
        },
        { html, pageId },
    );
}

/** Every file under `folder` whose name ends with `extension`. */
function findFiles(folder: string, extension: string): string[] {
    const found: string[] = [];
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
        const full = Path.join(folder, entry.name);
        if (entry.isDirectory()) found.push(...findFiles(full, extension));
        else if (entry.name.toLowerCase().endsWith(extension)) found.push(full);
    }
    return found;
}

/** The value of attribute `name` in the start tag `tag`, or undefined. */
function attribute(tag: string, name: string): string | undefined {
    return tag.match(new RegExp(`\\s${name}="([^"]*)"`))?.[1];
}

/**
 * The sentence ids, in order, that the staged ePUB's media overlay reads for the page holding
 * sentence `sentenceId`. The overlay is the SMIL file that the page's entry in the package
 * document (content.opf) names as its media-overlay, which is how a reader finds it; each <par>
 * there names the element its text is in by the fragment of its <text src>. Returns an
 * explanation instead when the page or its overlay is missing, so the comparison shows it.
 */
function getEpubOverlayIds(
    stagingFolder: string,
    sentenceId: string,
): string[] {
    const [opfFile] = findFiles(stagingFolder, ".opf");
    if (!opfFile) return [`(no package document in ${stagingFolder})`];
    const opfFolder = Path.dirname(opfFile);
    const items = (
        fs.readFileSync(opfFile, "utf8").match(/<item\s[^>]*>/g) ?? []
    ).map((tag) => ({
        id: attribute(tag, "id"),
        href: decodeURIComponent(attribute(tag, "href") ?? ""),
        overlay: attribute(tag, "media-overlay"),
    }));
    const pageItem = items.find(
        (item) =>
            item.href.endsWith("html") &&
            fs
                .readFileSync(Path.join(opfFolder, item.href), "utf8")
                .includes(`id="${sentenceId}"`),
    );
    if (!pageItem)
        return ["(no page of the ePUB holds the narrated sentences)"];
    const overlayItem = items.find((item) => item.id === pageItem.overlay);
    if (!overlayItem)
        return [`(the ePUB's page ${pageItem.href} names no media overlay)`];
    const smil = fs.readFileSync(
        Path.join(opfFolder, overlayItem.href),
        "utf8",
    );
    const pageFile = Path.basename(pageItem.href);
    return Array.from(smil.matchAll(/<text\s[^>]*>/g)).map(([tag]) => {
        const [file, id] = (attribute(tag, "src") ?? "").split("#");
        return Path.basename(decodeURIComponent(file)) === pageFile
            ? id
            : `(text in ${file}, not the page)`;
    });
}

/**
 * Open the ePUB screen, click PREVIEW, and return the folder the ePUB was staged in. The preview
 * is Readium, given the staged book's manifest as its `url` parameter; that manifest is
 * a localhost URL of a file in the staging folder (PublishEpubApi.SetupEpubControlContent).
 */
async function stageEpub(page: Page): Promise<string> {
    // The ePUB screen has no Text Languages list for openPublishDestination to wait for, and
    // its PREVIEW button has the same label as the BloomPUB screen's, so wait for a control only
    // the ePUB screen has before clicking it.
    await selectPublishDestination(page, "ePUB");
    await page
        .locator("#a11yCheckerLink")
        .waitFor({ state: "visible", timeout: 60000 });
    await page.locator('[aria-label="refresh preview"]').click();
    let manifestUrl: string | null = null;
    await expect
        .poll(
            () => {
                for (const frame of page.frames()) {
                    const url = new URL(frame.url(), "http://localhost/");
                    const manifest = url.searchParams.get("url");
                    if (manifest && url.pathname.includes("Readium")) {
                        manifestUrl = manifest;
                        return true;
                    }
                }
                return false;
            },
            {
                timeout: 180000,
                message: "The ePUB screen never showed its Readium preview.",
            },
        )
        .toBe(true);
    const prefix = manifestUrl!.match(/^https?:\/\/[^/]+\/bloom\//);
    if (!prefix)
        throw new Error(
            `The ePUB preview's manifest "${manifestUrl}" is not a localhost /bloom/ URL.`,
        );
    return Path.dirname(
        decodeURIComponent(manifestUrl!.substring(prefix[0].length)),
    );
}

test("the playback order set on a Canvas page is the order it plays in Edit, BloomPUB and ePUB [Test Case ID 487]", async ({
    page,
    step,
}) => {
    test.setTimeout(600000);
    elementIndexes.length = 0;

    const made = await step("Make a book with a Canvas page", async () => {
        const bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas");
        const [canvasPage] = await getContentPages(page);
        await goToPage(page, canvasPage.id);
        return { canvasPage, bookFolder };
    });
    if (!made) throw new Error("The book with a Canvas page was not made.");
    const { canvasPage, bookFolder } = made;

    await step(
        "Add a text block, two speech bubbles and a caption, each with a sentence",
        async () => {
            for (const element of kElements) {
                const index = await dragPaletteItemOntoCanvas(
                    page,
                    element.item,
                    { xFraction: element.at[0], yFraction: element.at[1] },
                );
                elementIndexes.push(index);
                await selectCanvasElement(page, index);
                await typeInCanvasElement(page, element.text);
            }
        },
    );

    const textById = await step("Record audio for each of them", async () => {
        await expandToolByHeader(page, "talkingBook");
        let sentences: Awaited<ReturnType<typeof getNarrationSentences>> = [];
        await expect
            .poll(
                async () => {
                    sentences = await getNarrationSentences(page);
                    return sentences.map((s) => s.text).sort();
                },
                {
                    timeout: 60000,
                    message:
                        "The Talking Book tool did not mark one sentence in each canvas element.",
                },
            )
            .toEqual([...kElements.map((e) => e.text)].sort());
        await addNarration(page, bookFolder, "en");
        // Leave the page and come back, so that the tool finds the narration it now has.
        const cover = (await getPages(page)).find((p) => !p.isContentPage)!;
        await goToPage(page, cover.id);
        await goToPage(page, canvasPage.id);
        return new Map(sentences.map((s) => [s.id, s.text]));
    });
    if (!textById) throw new Error("The canvas elements were not narrated.");

    await step("Turn on Show playback order buttons", async () => {
        await expandToolByHeader(page, "talkingBook");
        await setShowPlaybackOrder(page, true);
        await expect
            .poll(() => getShownPlaybackOrder(page), {
                timeout: 30000,
                message:
                    "Every canvas element should show its place in the playback order, in the order they were added.",
            })
            .toEqual(["1", "2", "3", "4"]);
    });

    await step("Change the playback order", async () => {
        // Alpha's "+" and then Delta's "-".
        await playbackOrderControls(page, 0)
            .locator(".playbackOrderButton")
            .nth(1)
            .click();
        await expect
            .poll(() => getShownPlaybackOrder(page))
            .toEqual(["2", "1", "3", "4"]);
        await playbackOrderControls(page, 3)
            .locator(".playbackOrderButton")
            .nth(0)
            .click();
        await expect
            .poll(() => getShownPlaybackOrder(page))
            .toEqual(["2", "1", "4", "3"]);
    });

    await step("Turn off Show playback order buttons", async () => {
        await setShowPlaybackOrder(page, false);
        await expect(
            editablePageFrame(page).locator(
                ".bloom-playbackOrderControlsContainer",
            ),
        ).toHaveCount(0, { timeout: 30000 });
    });

    await step(
        "Click Listen to the whole page and hear the boxes in the order chosen",
        async () => {
            const frames = [editablePageFrame(page), toolboxFrame(page)];
            for (const frame of frames) await recordFinishedPlays(frame);
            await toolboxFrame(page)
                .locator('[data-testid="talking-book-listen"] img')
                .click();
            expect(
                await waitForPlayedSentences(
                    frames,
                    textById,
                    kChosenOrder.length,
                    "Listen to the whole page",
                ),
            ).toEqual(kChosenOrder);
        },
    );

    await step(
        "See the BloomPUB carry each box's narration and the order chosen",
        async () => {
            await openPublishDestination(page, "BloomPUB");
            const stagedFolder = await stageBloomPub(page);
            expect(
                getStagedNarrationIds(stagedFolder),
                "The BloomPUB should carry the narration of every box.",
            ).toEqual([...textById.keys()].sort());
            expect(
                textsOf(
                    await getStagedPlaybackOrder(
                        page,
                        stagedFolder,
                        canvasPage.id,
                    ),
                    textById,
                ),
            ).toEqual(kChosenOrder);
        },
    );

    await step(
        "See the ePUB's media overlay read the boxes in the order chosen",
        async () => {
            const stagingFolder = await stageEpub(page);
            const ids = getEpubOverlayIds(
                stagingFolder,
                [...textById.keys()][0],
            );
            expect(textsOf(ids, textById)).toEqual(kChosenOrder);
        },
    );
});
