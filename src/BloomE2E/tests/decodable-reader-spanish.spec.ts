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

import type { Frame, Page } from "@playwright/test";
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
    editablePageFrame,
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
    setSampleTexts,
    setStageSightWords,
    setTypedSampleWords,
} from "../helpers/readerSetup";
import {
    expandReaderTool,
    expectReaderToolToShow,
    setReaderPhase,
    waitForReaderToolToRestore,
} from "../helpers/readerTools";
import { expectExpandedTool, toolboxFrame } from "../helpers/toolbox";

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

const SWITCH = '[data-testid="decodable-reader-switch"] input';
const STAGE_WORD = '[data-testid="decodable-reader-stage-word"]';
const GROUP = ".bloom-translationGroup";

/** The words the page's text box shows painted with one of the reader tools' highlights. */
async function getHighlightedWords(
    frame: Frame,
    highlight: string,
): Promise<string[]> {
    return frame.evaluate((name) => {
        const registry = (
            CSS as unknown as {
                highlights?: Map<string, Iterable<Range>>;
            }
        ).highlights;
        const ranges = registry?.get(name);
        return ranges ? [...ranges].map((range) => range.toString()) : [];
    }, highlight);
}

/** The words that the tool marks as not decodable at the stage it is on. */
async function getWordsMarkedNotDecodable(page: Page): Promise<string[]> {
    return (
        await getHighlightedWords(
            editablePageFrame(page),
            "bloom-reader-word-not-decodable",
        )
    ).sort();
}

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

/** The words the tool's panel lists under "Sample words in this stage", in the order shown. */
async function getStageWords(page: Page): Promise<string[]> {
    return toolboxFrame(page)
        .locator(STAGE_WORD)
        .evaluateAll((words) =>
            words.map((word) => (word.textContent ?? "").trim()),
        );
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
        await expect(
            toolboxFrame(page).locator(SWITCH),
            "A book made from the Decodable Reader template should be decodable from the start.",
        ).toBeChecked({ timeout: 30000 });
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
            .poll(async () => getStageWords(page), {
                timeout: 30000,
                message:
                    "At stage 1, the panel should list the sample-text words that need only " +
                    "a, m, s, e and l, the typed word aaa, and the sight word uno.",
            })
            .toEqual(
                expect.arrayContaining(["mesa", "sala", "ama", "aaa", "uno"]),
            );
        const stage1 = await getStageWords(page);
        expect(
            stage1,
            "pato needs p, t and o, which stage 1 does not teach",
        ).not.toContain("pato");
        expect(stage1, "bbb needs b, which only stage 4 teaches").not.toContain(
            "bbb",
        );

        await setReaderPhase(page, "decodableReader", 4);
        await expect
            .poll(async () => getStageWords(page), {
                timeout: 30000,
                message:
                    "At stage 4, which teaches b, the panel should list bbb.",
            })
            .toContain("bbb");
        await setReaderPhase(page, "decodableReader", 1);
    });

    await test.step("each sort button has a tip, and sorts the stage's words", async () => {
        const frame = toolboxFrame(page);
        const tips: [string, string][] = [
            ["decodable-reader-sort-alphabetic", "Sort alphabetically"],
            ["decodable-reader-sort-by-length", "Sort by word length"],
            ["decodable-reader-sort-by-frequency", "Sort by frequency"],
        ];
        for (const [testId, tip] of tips) {
            await frame.getByTestId(testId).hover();
            await expect(
                frame.getByRole("tooltip"),
                `Hovering the "${tip}" button should show its tip.`,
            ).toHaveText(tip, { timeout: 15000 });
            // Move off it, so the next tip is the only one.
            await frame.locator(SWITCH).hover();
            await expect(frame.getByRole("tooltip")).toHaveCount(0, {
                timeout: 15000,
            });
        }

        await frame.getByTestId("decodable-reader-sort-alphabetic").click();
        await expect
            .poll(async () => getStageWords(page), {
                timeout: 15000,
                message:
                    "Sorting alphabetically should put the stage's words in alphabetical order.",
            })
            .toEqual(["aaa", "ama", "mesa", "sala", "uno"]);

        await frame.getByTestId("decodable-reader-sort-by-length").click();
        await expect
            .poll(async () => getStageWords(page), {
                timeout: 15000,
                message:
                    "Sorting by word length should put the shortest words first, alphabetically " +
                    "among words of the same length.",
            })
            .toEqual(["aaa", "ama", "uno", "mesa", "sala"]);

        await frame.getByTestId("decodable-reader-sort-by-frequency").click();
        await expect
            .poll(async () => (await getStageWords(page)).slice(0, 2), {
                timeout: 15000,
                message:
                    "Sorting by frequency should put mesa (four times in the sample texts) " +
                    "first, then sala (twice).",
            })
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
        const toggle = toolboxFrame(page).locator(SWITCH);
        await toggle.click();
        await expect(toggle).not.toBeChecked({ timeout: 15000 });
        await expectMarkedNotDecodable(
            page,
            [],
            "With the book no longer decodable, no word should be marked.",
        );

        await toggle.click();
        await expect(toggle).toBeChecked({ timeout: 15000 });
        await expectMarkedNotDecodable(
            page,
            ["pato"],
            "Turning the book decodable again should put the mark on pato back straight away.",
            3000,
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

        await editBook(page, firstBook);
        await waitForEditablePage(page);
        await expandReaderTool(page, "decodableReader");
        await expectReaderToolToShow(
            page,
            "decodableReader",
            4,
            "The first book should still be on stage 4, where it was left.",
        );
        await editBook(page, secondBook);
        await expandReaderTool(page, "decodableReader");
        await expectReaderToolToShow(
            page,
            "decodableReader",
            2,
            "The second book should still be on stage 2, where it was left.",
        );
    });
});
