// The Leveled Reader's figures are right on a page with no text box, one text box, and two text
// boxes. Automates the manual test "Stats With Various Text Blocks" (Test Case ID 459): "Make sure
// it works with 0, 1, 2+ text blocks on page."
//
// The book has three pages: "Just an Image" (no text box), "Just Text" (one) and "Image in Middle"
// (two, one above the image and one below it). The one-box page holds one sentence of seven words.
// The two-box page holds a sentence of three words in its upper box and one of five in its lower
// box, so it has 8 words in 2 sentences. Were only one box of a page counted, that page would show
// 3 or 5 words and 1 sentence, and the book fewer than 15 words. The page with no text box must
// show no words or sentences of its own, and the book's figures, 15 words in 3 sentences (5.0 per
// sentence), must be the same seen from each page.
//
// The book's sentence total has a data-testid (leveled-reader-sentences-in-book) for this test.
//
// The case's note links BL-10445, which the team will not fix: on the Arithmetic template's pages
// the tool counts only the top row of boxes, because there the text box is not a direct child of
// its translation group. That page is not a page of "0, 1, 2+ text blocks" in the sense this case
// means, so this test leaves it out rather than expect what that bug does.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    expandReaderTool,
    makeBookLeveled,
    waitForLevelToBeRecordedInTheBook,
    waitForReaderToolToRestore,
} from "../helpers/readerTools";
import { enableToolForBook, toolboxFrame } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "leveled-reader-stats-text-blocks",
        languages: ["en"],
    },
});

/** The text boxes the reader tools count: an image description is not one of them. */
const TEXT_BOX_GROUP = ".bloom-translationGroup:not(.bloom-imageDescription)";
const LANG = "en";

/** One sentence of seven words, for the page with one text box. */
const ONE_BOX_TEXT = "One two three four five six seven.";
/** One sentence of three words, for the upper box of the page with two. */
const UPPER_BOX_TEXT = "Big red cat.";
/** One sentence of five words, for the lower box of the page with two. */
const LOWER_BOX_TEXT = "Blue dog ran far away.";

test("the Leveled Reader's figures are right on pages with 0, 1 and 2 text boxes (Test Case ID 459)", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just an Image");
    await addPage(page, "Just Text");
    await addPage(page, "Image in Middle");
    const [noBoxPage, oneBoxPage, twoBoxPage] = await getContentPages(page);
    // Before enableToolForBook, which goes back into the book, so the page comes up with the book
    // already leveled (see makeBookLeveled).
    await makeBookLeveled(page);
    await enableToolForBook(page, bookFolder, "leveledReader");
    await goToPage(page, oneBoxPage.id);
    await expandReaderTool(page, "leveledReader");
    await waitForReaderToolToRestore(page, "leveledReader", bookFolder);
    // Typing while Bloom reloads the page for the level would be lost.
    await waitForLevelToBeRecordedInTheBook(page, bookFolder);

    const textBoxes = editablePageFrame(page).locator(TEXT_BOX_GROUP);
    await expect(
        textBoxes,
        'The "Just Text" page should have one text box.',
    ).toHaveCount(1);
    await typeInGroup(page, TEXT_BOX_GROUP, LANG, ONE_BOX_TEXT);

    await goToPage(page, twoBoxPage.id);
    await expect(
        textBoxes,
        'The "Image in Middle" page should have two text boxes.',
    ).toHaveCount(2);
    await typeInGroup(page, TEXT_BOX_GROUP, LANG, UPPER_BOX_TEXT, 0);
    await typeInGroup(page, TEXT_BOX_GROUP, LANG, LOWER_BOX_TEXT, 1);

    const toolbox = toolboxFrame(page);
    const wordsThisPage = toolbox.getByTestId("leveled-reader-words-this-page");
    const sentencesThisPage = toolbox.getByTestId(
        "leveled-reader-sentences-this-page",
    );
    const wordsInBook = toolbox.getByTestId("leveled-reader-words-in-book");
    const sentencesInBook = toolbox.getByTestId(
        "leveled-reader-sentences-in-book",
    );
    const averagePerSentence = toolbox.getByTestId(
        "leveled-reader-average-words-per-sentence",
    );

    /** The book's figures, which are the same whichever page is showing. */
    const expectBookFigures = async (pageName: string) => {
        await expect(
            wordsInBook,
            `Seen from the ${pageName}, the book should have 15 words (7 + 3 + 5).`,
        ).toHaveText("15", { timeout: 15000 });
        await expect(
            sentencesInBook,
            `Seen from the ${pageName}, the book should have 3 sentences (1 + 1 + 1).`,
        ).toHaveText("3");
        await expect(
            averagePerSentence,
            `Seen from the ${pageName}, the book should average 5.0 words per sentence (15 in 3).`,
        ).toHaveText("5.0");
    };

    // Two text boxes: both count. The counts are updated after typing by the tool's markup pass,
    // so wait for them.
    await expect(
        wordsThisPage,
        "On the page with two text boxes, the Leveled Reader should count the words of both " +
            "boxes: 8 (3 + 5).",
    ).toHaveText("8", { timeout: 15000 });
    await expect(
        sentencesThisPage,
        "On the page with two text boxes, the Leveled Reader should count the sentence of each " +
            "box: 2.",
    ).toHaveText("2");
    await expectBookFigures("page with two text boxes");

    // One text box.
    await goToPage(page, oneBoxPage.id);
    await expect(
        wordsThisPage,
        "On the page with one text box, the Leveled Reader should count its 7 words.",
    ).toHaveText("7", { timeout: 15000 });
    await expect(
        sentencesThisPage,
        "On the page with one text box, the Leveled Reader should count its 1 sentence.",
    ).toHaveText("1");
    await expectBookFigures("page with one text box");

    // No text box.
    await goToPage(page, noBoxPage.id);
    await expect(
        textBoxes,
        'The "Just an Image" page should have no text box.',
    ).toHaveCount(0);
    await expect(
        wordsThisPage,
        "On the page with no text box, the Leveled Reader should count no words.",
    ).toHaveText("0", { timeout: 15000 });
    await expect(
        sentencesThisPage,
        "On the page with no text box, the Leveled Reader should count no sentences.",
    ).toHaveText("0");
    await expectBookFigures("page with no text box");
});
