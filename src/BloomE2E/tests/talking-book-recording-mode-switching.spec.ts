// The Talking Book tool's rules for switching between its two recording modes, "By Sentence" and
// "By Whole Text Box" [Test Case ID 482]:
//
//  - With no recording in the text box, a person can switch either way.
//  - Once the box holds a whole-text-box recording, "By Sentence" is disabled, and its tooltip says
//    to clear the recording first.
//  - After clearing it and recording by sentence, "By Whole Text Box" is still available. The
//    asymmetry is intended: a whole-text-box recording would be lost by splitting the box into
//    sentences, while sentence recordings stay playable until a new whole-box recording is made.
//
// Recording needs a microphone, so the two "record something" steps get their audio the ways the
// suite allows (helpers/talkingBook.ts): the whole-text-box recording through the tool's real
// Import Recording button, and the sentence recording by putting the mp3 where a recording of that
// sentence would go. Either way the test checks that the tool itself knows about the recording
// (its Check button is enabled) before judging the radios.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { setBranding } from "../helpers/collectionSettings";
import {
    addNarration,
    clickClearRecording,
    getRecordingMode,
    getRecordingModeTooltip,
    getTextBoxRecordingMode,
    importNarration,
    isCheckEnabled,
    isRecordingModeEnabled,
    openToolboxWithTalkingBook,
    setRecordingMode,
    waitForSentencesMarked,
} from "../helpers/talkingBook";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "talking-book-recording-mode-switching",
        languages: ["en"],
    },
});

const group = ".bloom-translationGroup";
const sentences = ["The cat sat down.", "The dog ran away."];

test("switches recording modes only as the rules allow [Test Case ID 482]", async ({
    page,
}) => {
    test.setTimeout(300000);

    // "By Whole Text Box" is a Pro-tier feature (FeatureRegistry's WholeTextBoxAudio), and so is
    // Import Recording, which this test uses to give the box a whole-text-box recording.
    await setBranding(page, "Sample-Pro");

    // The book keeps the folder Bloom made for it: it gets no title, and Bloom renames a book's
    // folder only after its title.
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const audioFolder = Path.join(bookFolder, "audio");
    await addPage(page, "Just Text", 1);
    await setContentLanguages(page, ["en"]);
    const contentPage = (await getContentPages(page))[0];
    await goToPage(page, contentPage.id);
    await typeInGroup(page, group, "en", sentences.join(" "));
    await openToolboxWithTalkingBook(page);
    await waitForSentencesMarked(page, group, "en", sentences);
    await clickInGroup(page, group, "en");

    await test.step("with no recording, both modes can be chosen, either way", async () => {
        expect(
            fs.existsSync(audioFolder) ? fs.readdirSync(audioFolder) : [],
            "The book had audio before anything was recorded.",
        ).toEqual([]);
        expect(
            await isRecordingModeEnabled(page, "By Sentence"),
            "By Sentence was disabled with no recording in the text box.",
        ).toBe(true);
        expect(
            await isRecordingModeEnabled(page, "By Whole Text Box"),
            "By Whole Text Box was disabled with no recording in the text box.",
        ).toBe(true);

        // Each switch is checked on the radio (setRecordingMode) and on the text box itself,
        // where Bloom stores the mode a person chose for it.
        await setRecordingMode(page, "By Whole Text Box");
        await expect
            .poll(() => getTextBoxRecordingMode(page, group, "en"), {
                message:
                    "Choosing By Whole Text Box did not set the text box's recording mode.",
            })
            .toBe("TextBox");
        await setRecordingMode(page, "By Sentence");
        await expect
            .poll(() => getTextBoxRecordingMode(page, group, "en"), {
                message:
                    "Choosing By Sentence did not set the text box's recording mode back.",
            })
            .toBe("Sentence");
    });

    await test.step("a whole-text-box recording disables By Sentence, with a tooltip saying to clear it", async () => {
        // importNarration chooses By Whole Text Box first, as the case's step does.
        await importNarration(page, bookFolder, group, "en");
        await expect
            .poll(() => isCheckEnabled(page), {
                timeout: 30000,
                message:
                    "The tool never enabled Check after the import, so it does not know the box has a recording.",
            })
            .toBe(true);
        expect(await getRecordingMode(page)).toBe("By Whole Text Box");

        await expect
            .poll(() => isRecordingModeEnabled(page, "By Sentence"), {
                timeout: 30000,
                message:
                    "By Sentence stayed enabled although the text box holds a whole-text-box recording.",
            })
            .toBe(false);
        expect(
            await getRecordingModeTooltip(page, "By Sentence"),
            "The disabled By Sentence radio's tooltip does not tell the person to clear the recording.",
        ).toBe(
            await getUiString(
                page,
                "EditTab.Toolbox.TalkingBookTool.RecordingModeDisabledBecauseHasAudioTip",
                'If you want to change this mode, first use the "Clear" button to remove your recording.',
            ),
        );
    });

    await test.step("after clearing and recording by sentence, By Whole Text Box can still be chosen", async () => {
        const recordings = fs.readdirSync(audioFolder);
        expect(
            recordings.length,
            `Expected one whole-text-box recording, got ${recordings}.`,
        ).toBe(1);
        const wholeBoxRecording = Path.join(audioFolder, recordings[0]);

        await clickClearRecording(page);
        await expect
            .poll(() => fs.existsSync(wholeBoxRecording), {
                timeout: 30000,
                message: `Clear did not delete the whole-text-box recording ${wholeBoxRecording}.`,
            })
            .toBe(false);
        await expect
            .poll(() => isRecordingModeEnabled(page, "By Sentence"), {
                timeout: 30000,
                message: "By Sentence stayed disabled after Clear.",
            })
            .toBe(true);

        await setRecordingMode(page, "By Sentence");
        const ids = await waitForSentencesMarked(page, group, "en", sentences);
        expect(
            await isCheckEnabled(page),
            "Check was enabled before any sentence had a recording.",
        ).toBe(false);

        // Record each sentence. The tool learns of a recording when it loads the page, so
        // leave the Edit tab and come back to the page.
        const recorded = await addNarration(page, bookFolder, "en");
        expect(recorded).toEqual(ids);
        await switchTab(page, "collection");
        await switchTab(page, "edit");
        await goToPage(page, contentPage.id);
        await openToolboxWithTalkingBook(page);
        await clickInGroup(page, group, "en");
        await expect
            .poll(() => isCheckEnabled(page), {
                timeout: 30000,
                message:
                    "The tool never enabled Check for the recorded sentence, so it does not know about the recording.",
            })
            .toBe(true);
        expect(await getRecordingMode(page)).toBe("By Sentence");

        // THE ASYMMETRY: sentence recordings do not lock the mode.
        expect(
            await isRecordingModeEnabled(page, "By Whole Text Box"),
            "By Whole Text Box was disabled although the box holds only sentence recordings.",
        ).toBe(true);
        await setRecordingMode(page, "By Whole Text Box");
        await expect
            .poll(() => getTextBoxRecordingMode(page, group, "en"), {
                message:
                    "Choosing By Whole Text Box over sentence recordings did not set the text box's recording mode.",
            })
            .toBe("TextBox");
    });
});
