// The Talking Book tool's Import Recording button: putting narration that was recorded elsewhere
// onto a page, instead of recording it from a microphone.
//
// This is the one file that drives that button. It exists for two reasons beyond the button
// itself: it is the journey coverage for Bloom's native "choose a file" dialog, which no test
// could reach until Bloom gained an --e2e way to pre-answer it (E2eTestingApi's nextChosenFile);
// and Import Recording is the only route by which a real person gets audio into a book without a
// microphone, so it is worth knowing it works.
//
// Every OTHER test that needs a book with narration uses addNarration instead, which just puts
// the mp3 where a recording would have gone. Import Recording is a poor way to seed audio: Bloom
// offers it only in whole-text-box recording mode, and only to a Pro subscription.
//
// The tests are serial, and the order is load-bearing: "By Sentence" mode can only be chosen
// while the box has no whole-text-box recording, so the test that checks that mode has to run
// before the one that imports.

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
    isImportRecordingEnabled,
    openToolboxWithTalkingBook,
    sampleNarrationFile,
    setRecordingMode,
} from "../helpers/talkingBook";

test.use({
    collectionSpec: { name: "import-recording", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BOOK_TITLE = "Import Recording Test";

// The book both tests work on, and where its audio would go. Read back from Bloom by the first
// test, which is the only thing that knows the folder Bloom chose.
let bookFolder: string;
let audioFolder: string;

test.describe("importing a recording made outside Bloom", () => {
    test("builds a book with one recordable sentence and grants the subscription", async ({
        page,
    }) => {
        test.setTimeout(300000);

        // Import Recording is a Pro-tier feature (FeatureRegistry's WholeTextBoxAudio), and a
        // real subscription code cannot be entered from a test, so grant the tier the way the e2e
        // hook allows. A descriptor ending in "-Pro" is what makes Bloom call it Pro.
        //
        // Granting it up front also matters for the test below: with the subscription in place,
        // the recording mode is the ONLY thing that can be disabling the button, which is the
        // whole point of that test.
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
        bookFolder = await findBookFolder(page, BOOK_TITLE);
        audioFolder = Path.join(bookFolder, "audio");

        // Sanity check: nothing is narrated yet, so a file appearing later is an import's work.
        expect(
            fs.existsSync(audioFolder) ? fs.readdirSync(audioFolder) : [],
            "The book already had audio files before anything was imported.",
        ).toEqual([]);
    });

    test("offers no Import Recording while recording by sentence [Test Case ID 488]", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;

        // By Sentence is the mode a book starts in, but choose it explicitly so the test states
        // what it is testing rather than relying on the default.
        await setRecordingMode(page, "By Sentence");
        expect(
            await isImportRecordingEnabled(page),
            "Import Recording was offered in By Sentence mode. Bloom imports a recording for a whole text box, so it has nothing to do in sentence mode.",
        ).toBe(false);

        // Sanity check the other half, so the test above cannot pass merely because the button is
        // disabled for some unrelated reason (a missing subscription, say, or an empty page).
        await setRecordingMode(page, "By Whole Text Box");
        expect(
            await isImportRecordingEnabled(page),
            "Import Recording stayed disabled even in whole-text-box mode, so the check above proved nothing.",
        ).toBe(true);
    });

    test("imports the chosen mp3 as the narration of the text box", async ({
        bloomApp,
    }) => {
        const page = bloomApp.page;
        test.setTimeout(180000);

        // THE ACTION UNDER TEST: the real Import Recording button, and the real file chooser
        // behind it, answered by the e2e hook rather than by a person.
        await importNarration(
            page,
            ".bloom-translationGroup",
            "en",
            sampleNarrationFile,
        );

        // Bloom names a narration file after the id of the text it belongs to, so the file that
        // appeared should be named after something the tool marked on this page.
        const sentences = await getNarrationSentences(page);
        const files = fs.readdirSync(audioFolder);
        expect(files.length, `Expected one narration file, got ${files}.`).toBe(
            1,
        );
        const importedId = Path.basename(files[0], Path.extname(files[0]));
        expect(
            sentences.map((s) => s.id),
            `The imported file "${files[0]}" is not named after any recordable element on the page.`,
        ).toContain(importedId);

        // And it is really the audio that was handed to the chooser, not an empty placeholder.
        expect(
            fs.statSync(Path.join(audioFolder, files[0])).size,
            "The imported narration file is empty.",
        ).toBe(fs.statSync(Path.resolve(sampleNarrationFile)).size);
    });
});
