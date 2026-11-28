// Typing with the Decodable Reader tool open, in a collection whose alphabet has a letter
// combination that contains a hyphen (BL-10324). The tool built a regular expression out of the
// collection's letters, and a hyphen in them made it invalid, so every check of the text threw.
// Those errors, one per key press, made the caret jump about while a person typed. A hyphen in a
// letter combination is the setup the user who reported it had; the manual case had the tester open
// that user's book. Automates the manual test "No Cursor Jumping" (Test Case ID 473).
//
// One test, because the setup dialog is saved: a save costs the next test in the same file its
// shell document ("Saving the reader settings costs the next test in the file its shell document"
// in AUTOMATION-DEBT.md).

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editablePageFrame,
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
 * The text typed. With b, o, d and -o taught, o-o, b-ob and ob-o are decodable; bob- and do-b are
 * not, because a hyphen is a letter here only as part of -o.
 */
const kTyped = "o-o b-ob bob- ob-o do-b";

/** This language's box in the page's text group, found without clicking in it. */
function textBox(page: Page, languageTag: string) {
    return editablePageFrame(page)
        .locator(GROUP)
        .first()
        .locator(`.bloom-editable[lang="${languageTag}"]`)
        .first();
}

/** Where the caret is in this language's box: its offset in the box's text. */
async function getCaretOffset(
    page: Page,
    languageTag: string,
): Promise<number> {
    return textBox(page, languageTag).evaluate((element) => {
        const selection = element.ownerDocument.getSelection();
        if (!selection || selection.rangeCount === 0)
            throw new Error("The page being edited has no selection.");
        const range = selection.getRangeAt(0);
        if (!range.collapsed)
            throw new Error(
                `The caret should be a single point, but "${range.toString()}" is selected.`,
            );
        const before = element.ownerDocument.createRange();
        before.selectNodeContents(element);
        before.setEnd(range.endContainer, range.endOffset);
        return before.toString().length;
    });
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
        await test.step("type a line of words, a key at a time", async () => {
            const box = await clickInGroup(page, GROUP, "en");
            // Real key presses: the tool checks the text on keyup.
            await page.keyboard.type(kTyped);
            await expect(
                box,
                "The text should be exactly what was typed; a caret that jumped while typing puts letters elsewhere.",
            ).toHaveText(kTyped);
            // The tool marks words only once it has checked the text, so the marks show that it
            // did, with the hyphen in its alphabet.
            await expect
                .poll(async () => getWordsMarkedNotDecodable(page), {
                    timeout: 15000,
                    message:
                        "The tool should mark bob- and do-b as not decodable, and o-o, b-ob and ob-o not.",
                })
                .toEqual(["bob-", "do-b"]);
            expect(
                await getCaretOffset(page, "en"),
                "The caret should still be at the end of what was typed.",
            ).toBe(kTyped.length);
        });

        await test.step("type a word in the middle of the line", async () => {
            const box = textBox(page, "en");
            // After "o-o b-ob ": in front of "bob-".
            const insertAt = "o-o b-ob ".length;
            await page.keyboard.press("Home");
            for (let i = 0; i < insertAt; i++)
                await page.keyboard.press("ArrowRight");
            expect(
                await getCaretOffset(page, "en"),
                "The arrow keys did not put the caret where the word is to go.",
            ).toBe(insertAt);
            await page.keyboard.type("dob ");
            const expected = `${kTyped.slice(0, insertAt)}dob ${kTyped.slice(insertAt)}`;
            await expect(
                box,
                "A word typed in the middle of the line should land where the caret was.",
            ).toHaveText(expected);
            await expect
                .poll(async () => getWordsMarkedNotDecodable(page), {
                    timeout: 15000,
                    message:
                        "dob is decodable, so the marks should not change.",
                })
                .toEqual(["bob-", "do-b"]);
            expect(
                await getCaretOffset(page, "en"),
                "The caret should be just after the word typed in the middle.",
            ).toBe(insertAt + "dob ".length);
        });

        expect(
            scriptErrors.errors(),
            "Checking the text with a hyphen in the alphabet should raise no script errors.",
        ).toEqual([]);
    } finally {
        scriptErrors.stop();
    }
});
