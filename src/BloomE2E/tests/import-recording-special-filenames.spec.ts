// Import Recording takes an mp3 whose file name holds characters that mean something in a URL or
// to the shell -- "+", "%", "#", "&", ";" -- or that are not ASCII, and the narration it makes
// plays back.
//
// Automates the manual test "Import Audio Special Filenames" (Test Case ID 493).
//
// Such names are a risk because the tool hands the chosen path to Bloom URL-encoded
// (audioRecording.ts importRecordingAsync posts it to fileIO/copyFile, which decodes it): a "+"
// read as a space, or a "%41" decoded a second time, names a file that is not there, and nothing
// is imported.
//
// Each name gets its own page, and each file holds different audio (the suite's sample repeated a
// different number of times), so a test can tell which file became which page's narration, and
// which one the tool played. A play is read from the toolbox's player: the file it started (Bloom
// names a narration file after the id of the text box it narrates), and the length of the audio
// it played to its end.
//
// Not checked: the case asks the tester to watch Bloom's Performance Measuring while doing this;
// that is a person's look at memory and timing, not a pass/fail check of the import.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Frame, Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { setBranding } from "../helpers/collectionSettings";
import {
    expectNarrationToBe,
    getNarrationSentences,
    importNarration,
    openToolboxWithTalkingBook,
    sampleNarrationFile,
} from "../helpers/talkingBook";
import { toolboxFrame } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "import-recording-special-filenames",
        languages: ["en"],
    },
});

const kGroup = ".bloom-translationGroup";

/**
 * The names of the files imported, one per page. Each is a name Windows allows, holding characters
 * that a URL, a query string or a decoder treats specially.
 */
const kFileNames = [
    // A "+" is a space to a form decoder.
    "plus+sign+take+1.mp3",
    // A "%41" or "%2B" decoded twice becomes "A" or "+".
    "percent 100% %41 %2B.mp3",
    // "#" ends a URL's path, "&" and ";" split a query, and the rest are quoted or reserved.
    "hash # amp & semi; comma, (paren) [bracket] 'apos' ~tilde $dollar =equals @at !bang.mp3",
    // Accented, non-Latin and combining characters.
    "naïve café ünï 日本語 नमस्ते.mp3",
];

/** One narration the toolbox's player played to its end. */
interface IPlay {
    /** The base name of the file it played, which is the id of the text box it narrates. */
    file: string;
    /** The length of the audio, in seconds. */
    duration: number;
}

/**
 * Have the toolbox's player note every narration it plays to its end, so that getPlays can read
 * them back. Starts the notes afresh.
 */
async function recordPlays(page: Page): Promise<void> {
    for (const frame of page.frames()) await recordPlaysIn(frame);
}

async function recordPlaysIn(frame: Frame): Promise<void> {
    await frame.evaluate(() => {
        interface IWindowWithPlays {
            e2eEndedPlays?: { url: string; duration: number }[];
            e2eListeningForPlays?: boolean;
        }
        const w = window as unknown as IWindowWithPlays;
        w.e2eEndedPlays = [];
        if (w.e2eListeningForPlays) return;
        w.e2eListeningForPlays = true;
        // "ended" does not bubble, so listen while it is on its way down to the player.
        document.addEventListener(
            "ended",
            (event) => {
                const media = event.target as HTMLMediaElement;
                w.e2eEndedPlays?.push({
                    url: media.currentSrc || media.src,
                    duration: media.duration,
                });
            },
            true,
        );
    });
}

/** The narrations that recordPlays noted as played to their end. */
async function getPlays(page: Page): Promise<IPlay[]> {
    const plays: IPlay[] = [];
    for (const frame of page.frames()) {
        const inFrame = await frame.evaluate(
            () =>
                (
                    window as unknown as {
                        e2eEndedPlays?: { url: string; duration: number }[];
                    }
                ).e2eEndedPlays ?? [],
        );
        for (const { url, duration } of inFrame) {
            // The tool plays a narration through Bloom's audio/wavFile API, which names the file
            // in its `id` parameter.
            const parsed = new URL(url);
            const path = decodeURIComponent(
                parsed.searchParams.get("id") ?? parsed.pathname,
            );
            const file = path.split(/[\\/]/).pop() ?? path;
            plays.push({ file: file.replace(/\.[^.]*$/, ""), duration });
        }
    }
    return plays;
}

/**
 * The tool's Check button, which plays the current text box's narration. It is found by its
 * picture: the buttons have no id, their labels are localized, and the button itself ignores the
 * pointer, so its picture takes the click.
 */
function checkButton(page: Page): Locator {
    return toolboxFrame(page)
        .locator(
            ["play_enabled.svg", "play_expected.svg", "pause_yellow.svg"]
                .map((p) => `button:has(img[src$="/${p}"])`)
                .join(", "),
        )
        .locator("img");
}

/** The length of an mp3, in seconds, as the browser decodes it. */
async function measureDuration(page: Page, mp3Path: string): Promise<number> {
    const base64 = fs.readFileSync(mp3Path).toString("base64");
    return await toolboxFrame(page).evaluate(async (data) => {
        const audio = new Audio(`data:audio/mpeg;base64,${data}`);
        await new Promise<void>((resolve, reject) => {
            audio.addEventListener("loadedmetadata", () => resolve());
            audio.addEventListener("error", () =>
                reject(new Error("The browser could not read the mp3.")),
            );
        });
        return audio.duration;
    }, base64);
}

test("imports and plays recordings whose file names hold special characters [Test Case ID 493]", async ({
    page,
}) => {
    test.setTimeout(600000);

    // Import Recording is a Pro-tier feature (FeatureRegistry's WholeTextBoxAudio).
    await setBranding(page, "Sample-Pro");

    // The files to import, in a folder of their own whose name is special too. File i holds the
    // sample i+1 times over, so each has audio of its own length.
    const sourceFolder = test.info().outputPath("audio+files & más");
    fs.mkdirSync(sourceFolder, { recursive: true });
    const sample = fs.readFileSync(Path.resolve(sampleNarrationFile));
    const sources = kFileNames.map((name, i) => {
        const path = Path.join(sourceFolder, name);
        fs.writeFileSync(
            path,
            Buffer.concat(Array.from({ length: i + 1 }, () => sample)),
        );
        return path;
    });

    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text", kFileNames.length);
    await setContentLanguages(page, ["en"]);
    const contentPages = await getContentPages(page);
    expect(
        contentPages.length,
        "The book should have one content page per file to import.",
    ).toBe(kFileNames.length);

    for (let i = 0; i < kFileNames.length; i++) {
        await test.step(`imports and plays "${kFileNames[i]}"`, async () => {
            await goToPage(page, contentPages[i].id);
            await typeInGroup(
                page,
                kGroup,
                "en",
                `Page ${i + 1} was recorded somewhere else.`,
            );
            await openToolboxWithTalkingBook(page);

            // THE ACTION UNDER TEST: the real Import Recording button, with the chooser answered
            // by the file with the special name.
            await importNarration(page, bookFolder, kGroup, "en", sources[i]);

            // The narration of this page's text box is that file's audio, byte for byte.
            let boxId = "";
            await expect
                .poll(
                    async () => {
                        const sentences = (
                            await getNarrationSentences(page)
                        ).filter((s) => s.languageTag === "en");
                        boxId = sentences.length === 1 ? sentences[0].id : "";
                        return fs.existsSync(
                            Path.join(bookFolder, "audio", `${boxId}.mp3`),
                        );
                    },
                    {
                        timeout: 15000,
                        message: `After importing "${kFileNames[i]}", the text box should be one recordable element with a narration file named after it.`,
                    },
                )
                .toBe(true);
            await expectNarrationToBe(
                bookFolder,
                boxId,
                sources[i],
                `The narration of page ${i + 1} should be the audio of "${kFileNames[i]}".`,
            );

            // And it plays: Check plays the box's narration to its end, and what it played is
            // that file's audio, by its length.
            const expectedDuration = await measureDuration(page, sources[i]);
            await recordPlays(page);
            await checkButton(page).click();
            let plays: IPlay[] = [];
            await expect
                .poll(
                    async () => {
                        plays = await getPlays(page);
                        return plays.length;
                    },
                    {
                        timeout: 60000,
                        message: `Check never played the narration imported from "${kFileNames[i]}" to its end.`,
                    },
                )
                .toBeGreaterThanOrEqual(1);
            expect(
                plays.length,
                `Check played more than one narration: ${JSON.stringify(plays)}`,
            ).toBe(1);
            expect(
                plays[0].file,
                "Check should play the narration of the text box.",
            ).toBe(boxId);
            expect(
                plays[0].duration,
                `Check should play the audio of "${kFileNames[i]}" (${expectedDuration} s).`,
            ).toBeCloseTo(expectedDuration, 1);
        });
    }

    // Sanity check: each page got a different file's audio, so the plays above could tell them
    // apart.
    const durations = await Promise.all(
        sources.map((s) => measureDuration(page, s)),
    );
    expect(
        new Set(durations.map((d) => d.toFixed(1))).size,
        `The files to import should differ in length: ${durations}`,
    ).toBe(kFileNames.length);
});
