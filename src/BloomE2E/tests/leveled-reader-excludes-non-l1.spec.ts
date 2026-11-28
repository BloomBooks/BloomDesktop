// The Leveled Reader's figures count only Language 1 text on the book's ordinary pages: text in
// another language, and the text of pages that Bloom leaves out of the reader statistics, add
// nothing. Automates the manual test "Exclude Non-L1 Pages" (Test Case ID 471): "Ensure non-L1
// pages and Instruction Pages aren't counted in stats."
//
// The book is bilingual, English (Language 1) and French, and has three pages:
// - "Just Text", whose box holds one English sentence of three words and two French sentences of
//   eight words. Only the English counts, so the page shows 3 words in 1 sentence.
// - "Translation Instructions" (Special template), a page of instructions to translators that shows
//   only in the Edit tab. Its box holds one sentence of five words.
// - "Comprehension Quiz" (Activity template), which Bloom marks bloom-ignoreForReaderStats. Its
//   question holds one English sentence of seven words, and its heading comes with "Check Your
//   Understanding" in English.
// So the book's figures, seen from the "Just Text" page, are those of that page alone: 3 words in
// 1 sentence (3.0 per sentence). Counting the French, the instructions or the quiz would raise them.
//
// Bloom gathers the book's text for these figures in ReadersApi.GetTextOfContentPagesAsJson, which
// takes only the visible bloom-content1 boxes of pages that are not front or back matter,
// nonprinting or bloom-ignoreForReaderStats.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    expandReaderTool,
    makeBookLeveled,
    waitForLevelToBeRecordedInTheBook,
    waitForReaderToolToRestore,
} from "../helpers/readerTools";
import { enableToolForBook, toolboxFrame } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "leveled-reader-excludes-non-l1",
        languages: ["en", "fr"],
        // The Comprehension Quiz is a game, and games need a subscription.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const L1 = "en";
const L2 = "fr";

/** The text boxes the reader tools count: an image description is not one of them. */
const TEXT_BOX_GROUP = ".bloom-translationGroup:not(.bloom-imageDescription)";
/** The quiz page's question, found by its style as in spreadsheet-quiz.spec.ts. */
const QUIZ_QUESTION_GROUP =
    ".bloom-translationGroup:is(.QuizQuestion-style, :has(> .QuizQuestion-style))";
/** The quiz page's heading, which Bloom fills with "Check Your Understanding". */
const QUIZ_HEADING_GROUP =
    ".bloom-translationGroup:is(.GameHeader-style, :has(> .GameHeader-style))";

/** One English sentence of three words, the only text that counts. */
const L1_TEXT = "Big red cat.";
/** Two French sentences of eight words, on the same page. */
const L2_TEXT = "Le petit chien court vite. Il aime jouer.";
/** One sentence of five words, for the instructions to translators. */
const INSTRUCTIONS_TEXT = "Translate every page with care.";
/** One English sentence of seven words, for the quiz question. */
const QUIZ_QUESTION_TEXT = "Where did the big red cat sleep?";

test("the Leveled Reader leaves other languages and instruction and quiz pages out of its figures (Test Case ID 471)", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    await addPage(page, "Translation Instructions");
    await addPage(page, "Comprehension Quiz");
    const [textPage, instructionsPage, quizPage] = await getContentPages(page);
    await goToPage(page, textPage.id);
    await setContentLanguages(page, [L1, L2]);
    // Before enableToolForBook, which goes back into the book, so the page comes up with the book
    // already leveled (see makeBookLeveled).
    await makeBookLeveled(page);
    await enableToolForBook(page, bookFolder, "leveledReader");
    await goToPage(page, textPage.id);
    await expandReaderTool(page, "leveledReader");
    await waitForReaderToolToRestore(page, "leveledReader", bookFolder);
    // Typing while Bloom reloads the page for the level would be lost.
    await waitForLevelToBeRecordedInTheBook(page, bookFolder);

    await typeInGroup(page, TEXT_BOX_GROUP, L1, L1_TEXT);
    await typeInGroup(page, TEXT_BOX_GROUP, L2, L2_TEXT);

    await goToPage(page, instructionsPage.id);
    const instructionsPageDiv = editablePageFrame(page).locator(".bloom-page");
    await expect(
        instructionsPageDiv,
        'The "Translation Instructions" page should be one that does not print.',
    ).toHaveClass(/\bbloom-nonprinting\b/);
    await typeInGroup(page, ".bloom-translationGroup", "*", INSTRUCTIONS_TEXT);

    await goToPage(page, quizPage.id);
    await expect(
        editablePageFrame(page).locator(".bloom-page"),
        'The "Comprehension Quiz" page should be one that the reader statistics ignore.',
    ).toHaveClass(/\bbloom-ignoreForReaderStats\b/);
    await expect(
        editablePageFrame(page)
            .locator(QUIZ_HEADING_GROUP)
            .locator(`.bloom-editable[lang="${L1}"]`),
        "The quiz's heading should come with English text, which must not count either.",
    ).toHaveText(/\S/);
    await typeInGroup(page, QUIZ_QUESTION_GROUP, L1, QUIZ_QUESTION_TEXT);

    await goToPage(page, textPage.id);
    // The quiz page shows the Game tool, so bring the Leveled Reader back.
    await expandReaderTool(page, "leveledReader");
    const pageFrame = editablePageFrame(page);
    await expect(
        pageFrame
            .locator(TEXT_BOX_GROUP)
            .locator(`.bloom-editable[lang="${L1}"]`),
        "The English text should still be on the page.",
    ).toHaveText(L1_TEXT);
    await expect(
        pageFrame
            .locator(TEXT_BOX_GROUP)
            .locator(`.bloom-editable[lang="${L2}"]`),
        "The French text should still be on the page and showing, so that the tool could count it.",
    ).toHaveText(L2_TEXT);
    await expect(
        pageFrame
            .locator(TEXT_BOX_GROUP)
            .locator(`.bloom-editable[lang="${L2}"]`),
    ).toBeVisible();

    const toolbox = toolboxFrame(page);
    await expect(
        toolbox.getByTestId("leveled-reader-words-this-page"),
        "The Leveled Reader should count only the 3 English words of the page, not the 8 French ones.",
    ).toHaveText("3", { timeout: 15000 });
    await expect(
        toolbox.getByTestId("leveled-reader-sentences-this-page"),
        "The Leveled Reader should count only the 1 English sentence of the page, not the 2 French ones.",
    ).toHaveText("1");
    await expect(
        toolbox.getByTestId("leveled-reader-words-in-book"),
        "The book should have only the 3 English words of the text page: not the French, the " +
            "instructions to translators or the quiz.",
    ).toHaveText("3", { timeout: 15000 });
    await expect(
        toolbox.getByTestId("leveled-reader-sentences-in-book"),
        "The book should have only the 1 English sentence of the text page.",
    ).toHaveText("1");
    await expect(
        toolbox.getByTestId("leveled-reader-average-words-per-sentence"),
        "The book should average 3.0 words per sentence (3 in 1).",
    ).toHaveText("3.0");
});
