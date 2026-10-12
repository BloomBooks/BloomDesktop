// The sentence Bloom writes on a derivative's credits page about the original book's copyright and
// license, and the padlock that lets the user reword it (BL-16859). Automates the manual test
// "Edit the Original Copyright and License Sentence" (Test Case ID 828).
//
// The test makes its own books: a Basic Book, which is not a derivative, and a derivative of "The
// Moon and the Cap" from the Sample Shells Bloom installs (Copyright © 2007, Pratham Books, CC BY
// 4.0), which is the book under test and is what a tester makes by hand too. Then it walks the
// card's rules in order: the Basic Book has no such sentence; the derivative's is locked and offers
// a closed padlock; clicking the padlock hands it over with the caret in it; the open padlock locks
// it again with the new words; leaving the page locks it too; "Not a translation or new version"
// hides it and unticking brings the user's words back; and the words survive quitting Bloom.
//
// The tests are serial: each starts from the book the one before it left behind, which is how the
// manual test reads too.
//
// Run with BLOOM_E2E_SCREENSHOT_DIR set to a folder and it also saves the pictures the Notion card
// shows under each verification (helpers/screenshot.ts, saveScreenshotIfAsked). Otherwise it saves
// none.

import * as fs from "node:fs";
import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromSourceBook,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    copyrightAndLicenseDialog,
    setNotATranslation,
} from "../helpers/copyrightAndLicense";
import {
    copyrightAndLicenseBlock,
    expectOriginalCopyrightSentence,
    goToCreditsPage,
    hoverOriginalCopyrightSentence,
    openCopyrightDialogFromCreditsPage,
    originalCopyrightHintBubble,
    originalCopyrightSentence,
    relockOriginalCopyrightSentence,
    typeInOriginalCopyrightSentence,
    unlockOriginalCopyrightSentence,
} from "../helpers/creditsPage";
import { saveScreenshotIfAsked } from "../helpers/screenshot";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "original-copyright-sentence", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The shell the derivative is made from, and what its copyright page says about it.
const SHELL_COLLECTION = "Sample Shells";
const SHELL_TITLE = "The Moon and the Cap";
const SHELL_COPYRIGHT_HOLDER = "Pratham Books";
// The bubble's words and the closed padlock's tooltip, as Bloom.xlf and BloomMediumPriority.xlf
// have them.
const HINT_LABEL = "Original copyright & license";
const UNLOCK_TOOLTIP = "Unlock to edit";
// The wording typed and then locked with the open padlock.
const FIRST_WORDING = "Adapted with permission from Pratham Books.";
// The wording typed and then locked by leaving the page; the rest of the test expects this one.
const SECOND_WORDING =
    "Translated from the English original by Pratham Books, used by permission.";

// Filled in as the tests go: the derivative's folder, and the sentence Bloom generated for it before
// the user took it over.
let derivativeFolder = "";
let generatedSentence = "";

test.describe("the sentence about the original book on a derivative's credits page", () => {
    test("a book that is not a derivative has no sentence about an original [Test Case ID 828]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await goToCreditsPage(page);
        await expectOriginalCopyrightSentence(
            page,
            { text: "", hasHintBubble: false },
            "A book made from Basic Book, which is not a derivative, showed a sentence about an original book.",
        );
        await saveScreenshotIfAsked(
            [copyrightAndLicenseBlock(page)],
            "01-basic-book-credits",
        );
    });

    test("a derivative's sentence names the original and is locked, with a closed padlock in its bubble [Test Case ID 828]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        // A person does this from the Collection tab; asked from the Edit tab, Bloom makes the
        // book but the Edit tab never loads a page of it.
        await switchTab(page, "collection");
        derivativeFolder = await makeBookFromSourceBook(
            page,
            SHELL_COLLECTION,
            SHELL_TITLE,
        );
        await goToCreditsPage(page);
        const sentence = await expectOriginalCopyrightSentence(
            page,
            { editable: false, hasHintBubble: true },
            "The derivative's credits page did not show a locked sentence about the original book.",
        );
        expect(
            sentence.text,
            "Bloom's sentence does not begin the way EditTab.FrontMatter.FullOriginalCopyrightLicenseSentence does",
        ).toMatch(/^This book is an adaptation of the original/);
        expect(
            sentence.text,
            "Bloom's sentence does not name the original book",
        ).toContain(SHELL_TITLE);
        expect(
            sentence.text,
            "Bloom's sentence does not name the original's copyright holder",
        ).toContain(SHELL_COPYRIGHT_HOLDER);
        generatedSentence = sentence.text;

        const bubble = await hoverOriginalCopyrightSentence(page);
        expect(bubble).toEqual({
            label: HINT_LABEL,
            padlock: "locked",
            padlockTooltip: UNLOCK_TOOLTIP,
        });
        await saveScreenshotIfAsked(
            [
                originalCopyrightSentence(page),
                originalCopyrightHintBubble(page),
            ],
            "02-locked-sentence-and-bubble",
        );
    });

    test("clicking the closed padlock makes the sentence editable, with the caret in it and Bloom's words [Test Case ID 828]", async ({
        page,
    }) => {
        // THE ACTION UNDER TEST: a real click on the padlock in the hint bubble.
        await unlockOriginalCopyrightSentence(page);
        await expectOriginalCopyrightSentence(
            page,
            { editable: true, hasFocus: true, text: generatedSentence },
            "After clicking the closed padlock, the sentence was not an editable box holding Bloom's words with the caret in it.",
        );

        const bubble = await hoverOriginalCopyrightSentence(page);
        expect(bubble).toEqual({
            label: HINT_LABEL,
            padlock: "unlocked",
            padlockTooltip: null,
        });
        await saveScreenshotIfAsked(
            [
                originalCopyrightSentence(page),
                originalCopyrightHintBubble(page),
            ],
            "03-unlocked-sentence-and-bubble",
        );
    });

    test("clicking the open padlock locks the sentence again, keeping the words typed [Test Case ID 828]", async ({
        page,
    }) => {
        await typeInOriginalCopyrightSentence(page, FIRST_WORDING);
        // THE ACTION UNDER TEST: a real click on the open padlock.
        await relockOriginalCopyrightSentence(page);
        await expectOriginalCopyrightSentence(
            page,
            { editable: false, hasHintBubble: true, text: FIRST_WORDING },
            "After typing new words and clicking the open padlock, the sentence was not locked with those words.",
        );
        expect((await hoverOriginalCopyrightSentence(page)).padlock).toBe(
            "locked",
        );
        await saveScreenshotIfAsked(
            [
                originalCopyrightSentence(page),
                originalCopyrightHintBubble(page),
            ],
            "04-relocked-with-new-words",
        );
    });

    test("leaving the page locks the sentence, keeping the words typed [Test Case ID 828]", async ({
        page,
    }) => {
        await unlockOriginalCopyrightSentence(page);
        await typeInOriginalCopyrightSentence(page, SECOND_WORDING);
        // THE ACTION UNDER TEST: leaving the credits page and coming back.
        await goToPage(page, (await getPages(page))[0].id);
        await goToCreditsPage(page);
        await expectOriginalCopyrightSentence(
            page,
            { editable: false, hasHintBubble: true, text: SECOND_WORDING },
            "After typing new words and leaving the page, the credits page did not show those words, locked.",
        );
        await saveScreenshotIfAsked(
            [originalCopyrightSentence(page)],
            "05-after-leaving-the-page",
        );
    });

    test('"Not a translation or new version" hides the sentence, and unticking it brings back the user\'s words [Test Case ID 828]', async ({
        page,
    }) => {
        test.setTimeout(300000);
        await openCopyrightDialogFromCreditsPage(page);
        await saveScreenshotIfAsked(
            [copyrightAndLicenseDialog(page)],
            "06-copyright-dialog",
        );
        await setNotATranslation(page, true);
        await expectOriginalCopyrightSentence(
            page,
            { text: "", hasHintBubble: false },
            'With "Not a translation or new version" ticked, the credits page still showed a sentence about the original book.',
        );
        await saveScreenshotIfAsked(
            [copyrightAndLicenseBlock(page)],
            "07-not-a-translation-no-sentence",
        );

        await openCopyrightDialogFromCreditsPage(page);
        await setNotATranslation(page, false);
        await expectOriginalCopyrightSentence(
            page,
            { editable: false, hasHintBubble: true, text: SECOND_WORDING },
            'After unticking "Not a translation or new version", the credits page did not show the user\'s words again.',
        );
        await saveScreenshotIfAsked(
            [originalCopyrightSentence(page)],
            "08-user-words-back",
        );
    });

    test("the user's words survive quitting and restarting Bloom [Test Case ID 828]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // Leave the credits page so Bloom writes it before it is stopped.
        await goToPage(page, (await getPages(page))[0].id);
        const afterRestart = await bloomApp.restart();
        expect(
            fs.existsSync(derivativeFolder),
            `The derivative's folder is gone after the restart: ${derivativeFolder}`,
        ).toBe(true);
        await selectBook(afterRestart, derivativeFolder);
        await switchTab(afterRestart, "edit");
        await goToCreditsPage(afterRestart);
        await expectOriginalCopyrightSentence(
            afterRestart,
            { editable: false, hasHintBubble: true, text: SECOND_WORDING },
            "After restarting Bloom, the credits page did not show the user's words, locked.",
        );
        expect(
            (await hoverOriginalCopyrightSentence(afterRestart)).padlock,
        ).toBe("locked");
        await saveScreenshotIfAsked(
            [
                originalCopyrightSentence(afterRestart),
                originalCopyrightHintBubble(afterRestart),
            ],
            "09-after-restart",
        );
    });
});
