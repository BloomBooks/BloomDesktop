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
    measureMp3Duration,
    narrationFilePath,
    openToolboxWithTalkingBook,
    playCurrentNarration,
    sampleNarrationFile,
} from "../helpers/talkingBook";

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

    // Sanity check: the files differ in length, so a play's length says which file it was.
    const durations: number[] = [];
    for (const source of sources)
        durations.push(await measureMp3Duration(page, source));
    expect(
        new Set(durations.map((d) => d.toFixed(1))).size,
        `The files to import should differ in length: ${durations}`,
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
                            narrationFilePath(bookFolder, boxId),
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
            const plays = await playCurrentNarration(page);
            expect(
                plays.length,
                `Check should play exactly one narration: ${JSON.stringify(plays)}`,
            ).toBe(1);
            expect(
                plays[0].file,
                "Check should play the narration of the text box.",
            ).toBe(boxId);
            expect(
                plays[0].duration,
                `Check should play the audio of "${kFileNames[i]}" (${durations[i]} s).`,
            ).toBeCloseTo(durations[i], 1);
        });
    }
});
