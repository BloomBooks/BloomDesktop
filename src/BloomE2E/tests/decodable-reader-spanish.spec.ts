// Building a Spanish decodable reader from scratch: a book made from the Decodable Reader template,
// the collection's stages set up in the Set Up Stages dialog with the Spanish alphabet and two
// Spanish sample texts, and then the tool at work on a page: which words it marks as not decodable,
// how that follows the switch, edits, pastes and the stage, how it sorts the stage's words, and
// which stage each book remembers. Automates the manual test "Build a Spanish Decodable From
// Scratch" (Test Case ID 427). Its "Generate Report" step is decodable-reader-report.spec.ts.
//
// Not checked here:
// - The template's link to the training videos. It opens the system browser, which a test cannot
//   see (the link goes through tiny.cc to Vimeo).
// - The "Reader Template Bloom Pack" section. Making the Bloom Pack is checked by
//   collection-menu.spec.ts (Test Case ID 660). Installing one runs only in a Bloom started with
//   the pack's path, and writes the pack's collection into the machine's own
//   %LOCALAPPDATA%\SIL\Bloom\Collections, which the developer's Bloom shares.
// - The word count. The Decodable Reader tool shows none; only the Leveled Reader counts words.
//
// One test, because the setup dialog is saved twice: a save costs the next test in the same file
// its shell document ("Saving the reader settings costs the next test in the file its shell
// document" in AUTOMATION-DEBT.md).

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    editBook,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    pasteTextInGroup,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    acceptReaderSetup,
    addStage,
    changeSavedReaderSettings,
    getSavedReaderSettings,
    openDecodableStagesSetup,
    openReaderSetupTab,
    selectStageLetter,
    setAlphabetLetters,
    setBookIsDecodable,
    setSampleTexts,
    setStageSightWords,
    setTypedSampleWords,
} from "../helpers/readerSetup";
import {
    clickDecodableSortButton,
    type DecodableSort,
    expandReaderTool,
    expectReaderToolToShow,
    getDecodableStageWords,
    getWordsMarkedNotDecodable,
    isReaderSwitchOn,
    kDecodableSortTips,
    readDecodableSortButtonTip,
    setReaderPhase,
    waitForReaderToolToRestore,
} from "../helpers/readerTools";
import { expectExpandedTool } from "../helpers/toolbox";
import { getUiStrings } from "../helpers/uiLanguage";

test.use({
    collectionSpec: { name: "spanish-decodable", languages: ["es"] },
});

/** The Spanish alphabet, as the manual test has the tester paste it. */
const kSpanishLetters =
    "a á b c d e é f g h i í j k l m n ñ o ó p q r s t u ú v w x y z";

/** The letters each stage teaches, and its new sight words. */
const kStages = [
    { letters: ["a", "m", "s", "e", "l"], sightWords: "uno" },
    { letters: ["o", "p", "t"], sightWords: "dos" },
    { letters: ["i", "n", "d", "u"], sightWords: "" },
    { letters: ["c", "r", "b", "ñ"], sightWords: "" },
];

/**
 * Two Spanish sample texts. At stage 1 (a, m, s, e, l) the words that need nothing else are mesa
 * (four times in all), sala (twice) and ama (once); pato and lata need p, t or o (stage 2), and
 * niño needs n, i and ñ (stages 3 and 4).
 */
const kSampleTexts = {
    "cuento uno.txt": "mesa mesa sala lata pato ama",
    "cuento dos.txt": "mesa mesa sala pato niño",
};

const GROUP = ".bloom-translationGroup";

/** Wait until the tool marks exactly these words, in any order, as not decodable. */
async function expectMarkedNotDecodable(
    page: Page,
    words: string[],
    message: string,
    timeout = 15000,
): Promise<void> {
    await expect
        .poll(async () => getWordsMarkedNotDecodable(page), {
            timeout,
            message,
        })
        .toEqual([...words].sort());
}

test("a Spanish decodable reader built from scratch marks, sorts and remembers its stage [Test Case ID 427]", async ({
    page,
    bloomApp,
}) => {
    const firstBook = await makeBookFromTemplate(page, "Decodable Reader");
    await addPage(page, "Basic Text & Image");

    await test.step("a book made from the template opens the tool, with the book decodable", async () => {
        await expectExpandedTool(
            page,
            "decodableReader",
            "A book made from the Decodable Reader template should open on the Decodable Reader tool.",
        );
        await expect
            .poll(async () => isReaderSwitchOn(page, "decodableReader"), {
                timeout: 30000,
                message:
                    "A book made from the Decodable Reader template should be decodable from the start.",
            })
            .toBe(true);
    });

    await test.step("set up four stages with the Spanish alphabet and two sample texts", async () => {
        // A new collection gets the stages the machine has for its language, so start from none,
        // as a collection does that nobody has set up.
        await changeSavedReaderSettings(page, (settings) => {
            settings.letters = "";
            settings.moreWords = "";
            settings.useAllowedWords = 0;
            settings.stages = [{ name: "1", letters: "", sightWords: "" }];
        });
        setSampleTexts(bloomApp.collectionDir, kSampleTexts);

        await openDecodableStagesSetup(page);
        await openReaderSetupTab(page, "letters");
        await setAlphabetLetters(page, kSpanishLetters);
        await openReaderSetupTab(page, "stages");
        for (const [index, stage] of kStages.entries()) {
            if (index > 0) await addStage(page);
            for (const letter of stage.letters)
                await selectStageLetter(page, letter);
            if (stage.sightWords)
                await setStageSightWords(page, stage.sightWords);
        }
        await acceptReaderSetup(page);

        const saved = await getSavedReaderSettings(page);
        expect(saved.letters.split(" ")).toEqual(kSpanishLetters.split(" "));
        expect(
            saved.stages.map((stage) => stage.letters.split(" ").sort()),
        ).toEqual(kStages.map((stage) => [...stage.letters].sort()));
    });

    await test.step("bogus words typed as sample words show among the stage's words", async () => {
        await openDecodableStagesSetup(page);
        await openReaderSetupTab(page, "sampleWords");
        await setTypedSampleWords(page, "aaa bbb");
        await acceptReaderSetup(page);

        await expandReaderTool(page, "decodableReader");
        await setReaderPhase(page, "decodableReader", 1);
        await expect
            .poll(async () => getDecodableStageWords(page), {
                timeout: 30000,
                message:
                    "At stage 1, the panel should list the sample-text words that need only " +
                    "a, m, s, e and l, the typed word aaa, and the sight word uno.",
            })
            .toEqual(
                expect.arrayContaining(["mesa", "sala", "ama", "aaa", "uno"]),
            );
        const stage1 = await getDecodableStageWords(page);
        expect(
            stage1,
            "pato needs p, t and o, which stage 1 does not teach",
        ).not.toContain("pato");
        expect(stage1, "bbb needs b, which only stage 4 teaches").not.toContain(
            "bbb",
        );

        await setReaderPhase(page, "decodableReader", 4);
        await expect
            .poll(async () => getDecodableStageWords(page), {
                timeout: 30000,
                message:
                    "At stage 4, which teaches b, the panel should list bbb.",
            })
            .toContain("bbb");
        await setReaderPhase(page, "decodableReader", 1);
    });

    await test.step("each sort button has a tip, and sorts the stage's words", async () => {
        const sorts = Object.keys(kDecodableSortTips) as DecodableSort[];
        // The tips in the UI language Bloom is running in.
        const tips = await getUiStrings(
            page,
            Object.fromEntries(
                sorts.map((sort) => [
                    kDecodableSortTips[sort].id,
                    kDecodableSortTips[sort].english,
                ]),
            ),
        );
        for (const sort of sorts) {
            expect(
                await readDecodableSortButtonTip(page, sort),
                `Hovering the ${sort} sort button should show its tip.`,
            ).toBe(tips[kDecodableSortTips[sort].id]);
        }

        await clickDecodableSortButton(page, "alphabetic");
        await expect
            .poll(async () => getDecodableStageWords(page), {
                timeout: 15000,
                message:
                    "Sorting alphabetically should put the stage's words in alphabetical order.",
            })
            .toEqual(["aaa", "ama", "mesa", "sala", "uno"]);

        await clickDecodableSortButton(page, "by-length");
        await expect
            .poll(async () => getDecodableStageWords(page), {
                timeout: 15000,
                message:
                    "Sorting by word length should put the shortest words first, alphabetically " +
                    "among words of the same length.",
            })
            .toEqual(["aaa", "ama", "uno", "mesa", "sala"]);

        await clickDecodableSortButton(page, "by-frequency");
        await expect
            .poll(
                async () => (await getDecodableStageWords(page)).slice(0, 2),
                {
                    timeout: 15000,
                    message:
                        "Sorting by frequency should put mesa (four times in the sample texts) " +
                        "first, then sala (twice).",
                },
            )
            .toEqual(["mesa", "sala"]);
    });

    const [textPage] = await getContentPages(page);
    await goToPage(page, textPage.id);
    await expandReaderTool(page, "decodableReader");
    await waitForReaderToolToRestore(page, "decodableReader", firstBook);

    await test.step("the tool marks the words that are not decodable at the stage", async () => {
        const box = await clickInGroup(page, GROUP, "es");
        // Real key presses: the tool marks the text up on keyup.
        await page.keyboard.type("mesa pato sala");
        await expect(box).toHaveText("mesa pato sala");
        await expectMarkedNotDecodable(
            page,
            ["pato"],
            "At stage 1, pato should be marked as not decodable, and mesa and sala not.",
        );
    });

    await test.step("turning the switch off removes the marks, and on puts them back at once", async () => {
        await setBookIsDecodable(page, false);
        await expectMarkedNotDecodable(
            page,
            [],
            "With the book no longer decodable, no word should be marked.",
        );

        // Three seconds from the click, which setBookIsDecodable makes before it waits for the
        // panel to redraw.
        const clicked = Date.now();
        await setBookIsDecodable(page, true);
        await expectMarkedNotDecodable(
            page,
            ["pato"],
            "Turning the book decodable again should put the mark on pato back within three seconds.",
            Math.max(1, 3000 - (Date.now() - clicked)),
        );
    });

    await test.step("the marks follow edits inside a word, pastes and replaced words", async () => {
        const box = await clickInGroup(page, GROUP, "es");
        // Put a t inside mesa: "mesa" -> "metsa", which needs a letter stage 1 does not teach.
        await page.keyboard.press("Control+Home");
        await page.keyboard.press("ArrowRight");
        await page.keyboard.press("ArrowRight");
        await page.keyboard.type("t");
        await expect(box).toHaveText("metsa pato sala");
        await expectMarkedNotDecodable(
            page,
            ["metsa", "pato"],
            "A t typed inside mesa should make it not decodable.",
        );
        await page.keyboard.press("Backspace");
        await expect(box).toHaveText("mesa pato sala");
        await expectMarkedNotDecodable(
            page,
            ["pato"],
            "Taking the t out again should make mesa decodable again.",
        );

        await pasteTextInGroup(page, GROUP, "es", "sala lata ama");
        await expectMarkedNotDecodable(
            page,
            ["lata"],
            "In pasted text, lata should be marked and sala and ama not.",
        );

        // Replace a word: select "ama" and type over it.
        await clickInGroup(page, GROUP, "es");
        await page.keyboard.press("End");
        for (let i = 0; i < 3; i++)
            await page.keyboard.press("Shift+ArrowLeft");
        await page.keyboard.type("mesa");
        await expect(box).toHaveText("sala lata mesa");
        await page.keyboard.type(" pato");
        await expect(box).toHaveText("sala lata mesa pato");
        await expectMarkedNotDecodable(
            page,
            ["lata", "pato"],
            "After ama was replaced and pato typed, lata and pato should be the marked words.",
        );
    });

    await test.step("the marks follow the stage", async () => {
        await setReaderPhase(page, "decodableReader", 2);
        await expectMarkedNotDecodable(
            page,
            [],
            "Stage 2 teaches o, p and t, so lata and pato should be decodable there.",
        );
        await setReaderPhase(page, "decodableReader", 1);
        await expectMarkedNotDecodable(
            page,
            ["lata", "pato"],
            "Back at stage 1, lata and pato should be marked again.",
        );
    });

    await test.step("a new book starts on the stage last chosen, and each book keeps its own", async () => {
        await setReaderPhase(page, "decodableReader", 4);
        const secondBook = await makeBookFromTemplate(page, "Decodable Reader");
        await expandReaderTool(page, "decodableReader");
        await expectReaderToolToShow(
            page,
            "decodableReader",
            4,
            "A new book from the Decodable Reader template should start on stage 4, the stage last chosen.",
        );
        await setReaderPhase(page, "decodableReader", 2);

        // Each book is reopened while the stage last chosen is some other stage, so that a book
        // that lost its own stage and fell back to that one would show the wrong stage.
        await editBook(page, firstBook);
        await waitForEditablePage(page);
        await expandReaderTool(page, "decodableReader");
        await expectReaderToolToShow(
            page,
            "decodableReader",
            4,
            "The first book should still be on stage 4, where it was left, not stage 2, the stage last chosen.",
        );
        await setReaderPhase(page, "decodableReader", 3);
        await editBook(page, secondBook);
        await expandReaderTool(page, "decodableReader");
        await expectReaderToolToShow(
            page,
            "decodableReader",
            2,
            "The second book should still be on stage 2, where it was left, not stage 3, the stage last chosen.",
        );
    });
});
