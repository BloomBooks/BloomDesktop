// Typing with the Decodable Reader tool open, in a collection whose alphabet has a letter
// combination that contains a hyphen (BL-10324). The tool built a regular expression out of the
// collection's letters, and a hyphen in them made it invalid, so every check of the text threw.
// Those errors, one per key press, made the caret jump about while a person typed. A hyphen in a
// letter combination is the setup the user who reported it had; the manual case had the tester open
// that user's book. Automates the manual test "No Cursor Jumping" (Test Case ID 473).
//
// The tool checks the text in a markup pass half a second after the typing stops
// (handlePageEditing in pageEditingMarkup.ts), and that pass is where the caret could jump. So each
// step types something that changes which words are marked, waits for the marks to change, and
// only then reads where the caret is.
//
// One test, because the setup dialog is saved: a save costs the next test in the same file its
// shell document ("Saving the reader settings costs the next test in the file its shell document"
// in AUTOMATION-DEBT.md).

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getCaretOffset,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    acceptReaderSetup,
    changeSavedReaderSettings,
    getSavedReaderSettings,
    openDecodableStagesSetup,
    openReaderSetupTab,
    selectStageLetter,
    setAlphabetLetters,
} from "../helpers/readerSetup";
import {
    expandReaderTool,
    getWordsMarkedNotDecodable,
    waitForReaderToolToRestore,
} from "../helpers/readerTools";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: { name: "decodable-hyphen-letter", languages: ["en"] },
});

/** The alphabet: three letters and "-o", a letter combination that starts with a hyphen. */
const kLetters = ["b", "o", "d", "-o"];

const GROUP = ".bloom-translationGroup";

/**
 * The text typed first. With b, o, d and -o taught, o-o, b-ob and ob-o are decodable; bob- and
 * do-b are not, because a hyphen is a letter here only as part of -o.
 */
const kTyped = "o-o b-ob bob- ob-o do-b";

/** The word typed in the middle of the line afterwards; not decodable, for the same reason. */
const kInserted = "bo-b ";

/** Wait until the tool marks exactly these words as not decodable. */
async function expectMarkedNotDecodable(
    page: Page,
    words: string[],
    message: string,
): Promise<void> {
    await expect
        .poll(async () => getWordsMarkedNotDecodable(page), {
            timeout: 15000,
            message,
        })
        .toEqual([...words].sort());
}

test("typing with the Decodable Reader open keeps the caret where it is when a letter combination has a hyphen [Test Case ID 473]", async ({
    page,
}) => {
    const book = await makeBookFromTemplate(page, "Decodable Reader");
    await addPage(page, "Basic Text & Image");

    await test.step("give the collection an alphabet with a hyphen in a letter combination, all taught at stage 1", async () => {
        // A new collection gets the stages the machine has for its language, so start from none.
        await changeSavedReaderSettings(page, (settings) => {
            settings.letters = "";
            settings.moreWords = "";
            settings.useAllowedWords = 0;
            settings.stages = [{ name: "1", letters: "", sightWords: "" }];
        });
        await openDecodableStagesSetup(page);
        await openReaderSetupTab(page, "letters");
        await setAlphabetLetters(page, kLetters.join(" "));
        await openReaderSetupTab(page, "stages");
        for (const letter of kLetters) await selectStageLetter(page, letter);
        await acceptReaderSetup(page);

        const saved = await getSavedReaderSettings(page);
        expect(saved.letters.split(" ")).toEqual(kLetters);
        expect(
            saved.stages.map((stage) => stage.letters.split(" ").sort()),
        ).toEqual([[...kLetters].sort()]);
    });

    const [textPage] = await getContentPages(page);
    await goToPage(page, textPage.id);
    await expandReaderTool(page, "decodableReader");
    await waitForReaderToolToRestore(page, "decodableReader", book);

    const scriptErrors = watchForScriptErrors(page);
    try {
        const box = await clickInGroup(page, GROUP, "en");

        await test.step("type a line of words, a key at a time", async () => {
            // Real key presses: the tool checks the text on keyup.
            await page.keyboard.type(kTyped);
            await expect(
                box,
                "The text should be exactly what was typed; a caret that jumped while typing puts letters elsewhere.",
            ).toHaveText(kTyped);
            // The marks need the whole line, so they show that the pass after the last key ran,
            // with the hyphen in the tool's alphabet.
            await expectMarkedNotDecodable(
                page,
                ["bob-", "do-b"],
                "The tool should mark bob- and do-b as not decodable, and o-o, b-ob and ob-o not.",
            );
            expect(
                await getCaretOffset(box),
                "The caret should still be at the end of what was typed.",
            ).toBe(kTyped.length);
        });

        await test.step("type a word in the middle of the line", async () => {
            // After "o-o b-ob ": in front of "bob-".
            const insertAt = "o-o b-ob ".length;
            await page.keyboard.press("Home");
            for (let i = 0; i < insertAt; i++)
                await page.keyboard.press("ArrowRight");
            expect(
                await getCaretOffset(box),
                "The arrow keys did not put the caret where the word is to go.",
            ).toBe(insertAt);
            await page.keyboard.type(kInserted);
            await expect(
                box,
                "A word typed in the middle of the line should land where the caret was.",
            ).toHaveText(
                kTyped.slice(0, insertAt) + kInserted + kTyped.slice(insertAt),
            );
            await expectMarkedNotDecodable(
                page,
                ["bo-b", "bob-", "do-b"],
                "The tool should mark bo-b, typed in the middle, as not decodable as well.",
            );
            expect(
                await getCaretOffset(box),
                "After the tool checked the text, the caret should still be just after the word typed in the middle.",
            ).toBe(insertAt + kInserted.length);
        });

        expect(
            scriptErrors.errors(),
            "Checking the text with a hyphen in the alphabet should raise no script errors.",
        ).toEqual([]);
    } finally {
        scriptErrors.stop();
    }
});
