// The Talking Book tool's Import Recording button: putting narration that was recorded elsewhere
// onto a page, instead of recording it from a microphone.
//
// This is the one test that drives that button. It exists for two reasons beyond the button
// itself: it is the journey coverage for Bloom's "choose a file" dialog, which no test could
// reach until Bloom gained an --e2e way to pre-answer it (E2eTestingApi's nextChosenFile); and
// Import Recording is the only route by which a real person gets audio into a book without a
// microphone, so it is worth knowing it works.
//
// Every OTHER test that needs a book with narration uses addNarration instead, which just puts
// the mp3 where a recording would have gone. Import Recording is a poor way to seed audio: Bloom
// offers it only in whole-text-box recording mode, and only to a Pro subscription.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { setBranding } from "../helpers/collectionSettings";
import {
    getNarrationSentences,
    importNarration,
    openToolboxWithTalkingBook,
    sampleNarrationFile,
} from "../helpers/talkingBook";

test.use({
    collectionSpec: { name: "import-recording", languages: ["en"] },
});

const BOOK_TITLE = "Import Recording Test";

test("imports a recording made outside Bloom, through the real file chooser", async ({
    page,
}) => {
    test.setTimeout(300000);

    // Import Recording is a Pro-tier feature (FeatureRegistry's WholeTextBoxAudio), and a real
    // subscription code cannot be entered from a test, so grant the tier the way the e2e hook
    // allows. A descriptor ending in "-Pro" is what makes Bloom call it Pro.
    await setBranding(page, "Sample-Pro");

    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
    await addPage(page, "Just Text", 1);
    await setContentLanguages(page, ["en"]);
    const contentPages = await getContentPages(page);
    await goToPage(page, contentPages[0].id);
    await typeInGroup(
        page,
        ".bloom-translationGroup",
        "en",
        "This sentence was recorded somewhere else.",
    );

    await openToolboxWithTalkingBook(page);
    const bookFolder = await findBookFolder(page, BOOK_TITLE);

    // Sanity check: nothing is narrated yet, so a file appearing below is this import's work.
    const audioFolder = Path.join(bookFolder, "audio");
    expect(
        fs.existsSync(audioFolder) ? fs.readdirSync(audioFolder) : [],
        "The book already had audio files before anything was imported.",
    ).toEqual([]);

    // THE ACTION UNDER TEST: the real Import Recording button, and the real file chooser behind
    // it, answered by the e2e hook rather than by a person.
    await importNarration(
        page,
        ".bloom-translationGroup",
        "en",
        sampleNarrationFile,
    );

    // Bloom names a narration file after the id of the text it belongs to, so the file that
    // appeared should be named after a sentence the tool marked on this page.
    const sentences = await getNarrationSentences(page);
    const files = fs.readdirSync(audioFolder);
    expect(files.length, `Expected one narration file, got ${files}.`).toBe(1);
    const importedId = Path.basename(files[0], Path.extname(files[0]));
    expect(
        sentences.map((s) => s.id),
        `The imported file "${files[0]}" is not named after any sentence on the page.`,
    ).toContain(importedId);

    // And it is really the audio we handed the chooser, not an empty placeholder.
    expect(
        fs.statSync(Path.join(audioFolder, files[0])).size,
        "The imported narration file is smaller than the mp3 that was chosen.",
    ).toBeGreaterThan(0);
});
