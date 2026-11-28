// The Leveled Reader's whole-book figures follow the book when a page is removed. Automates the
// manual test "Leveled Reader Stats Refresh" (Test Case ID 470): type text in a leveled reader,
// observe the stats, delete the page, and the stats should refresh.
//
// The book has two pages of text that give different figures: the first page one sentence of
// seven words, the second two sentences of five words in all. With both, the book has 12 words in
// 3 sentences (4.0 per sentence). Removing the second page leaves 7 words in 1 sentence (7.0 per
// sentence). Were the figures not refreshed, the tool would still count the removed page's words.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    confirmRemovePageDialog,
    openRemovePageDialog,
} from "../helpers/pageList";
import {
    expandReaderTool,
    makeBookLeveled,
    waitForLevelToBeRecordedInTheBook,
    waitForReaderToolToRestore,
} from "../helpers/readerTools";
import { enableToolForBook, toolboxFrame } from "../helpers/toolbox";

test.use({
    collectionSpec: {
        name: "leveled-reader-stats-refresh",
        languages: ["en"],
    },
});

const GROUP = ".bloom-translationGroup";
const LANG = "en";

/** One sentence of seven words. */
const FIRST_PAGE_TEXT = "One two three four five six seven.";
/** Two sentences, five words in all. */
const SECOND_PAGE_TEXT = "Big red cat. Blue dog.";

test("the Leveled Reader's book figures drop the words of a removed page (Test Case ID 470)", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    await addPage(page, "Just Text");
    const [firstPage, secondPage] = await getContentPages(page);
    // Before enableToolForBook, which goes back into the book, so the page comes up with the book
    // already leveled (see makeBookLeveled).
    await makeBookLeveled(page);
    await enableToolForBook(page, bookFolder, "leveledReader");
    await goToPage(page, firstPage.id);
    await expandReaderTool(page, "leveledReader");
    await waitForReaderToolToRestore(page, "leveledReader", bookFolder);
    // Typing while Bloom reloads the page for the level would be lost.
    await waitForLevelToBeRecordedInTheBook(page, bookFolder);

    await typeInGroup(page, GROUP, LANG, FIRST_PAGE_TEXT);
    await goToPage(page, secondPage.id);
    await typeInGroup(page, GROUP, LANG, SECOND_PAGE_TEXT);

    const toolbox = toolboxFrame(page);
    const wordsThisPage = toolbox.getByTestId("leveled-reader-words-this-page");
    const wordsInBook = toolbox.getByTestId("leveled-reader-words-in-book");
    const averagePerSentence = toolbox.getByTestId(
        "leveled-reader-average-words-per-sentence",
    );

    // Observe the stats with both pages in the book. The counts are updated after typing by the
    // tool's markup pass, so wait for them.
    await expect(
        wordsThisPage,
        "The Leveled Reader should count the 5 words typed on the second page.",
    ).toHaveText("5", { timeout: 15000 });
    await expect(
        wordsInBook,
        "With both pages, the Leveled Reader should count 12 words in the book (7 + 5).",
    ).toHaveText("12", { timeout: 15000 });
    await expect(
        averagePerSentence,
        "With both pages, the book should average 4.0 words per sentence (12 in 3).",
    ).toHaveText("4.0");

    // Delete the page being edited, the second page.
    await openRemovePageDialog(page);
    await confirmRemovePageDialog(page);
    const remaining = await getContentPages(page);
    expect(
        remaining.map((p) => p.id),
        "Only the first page of text should be left in the book.",
    ).toEqual([firstPage.id]);
    await expect(
        wordsInBook,
        "After the second page was removed, the Leveled Reader should count only the first " +
            "page's 7 words in the book.",
    ).toHaveText("7", { timeout: 15000 });
    await expect(
        averagePerSentence,
        "After the second page was removed, the book should average 7.0 words per sentence " +
            "(its one sentence of 7 words).",
    ).toHaveText("7.0");

    // Bloom shows another page in place of the removed one (not always the first page of text).
    // Go to the remaining page of text: the page's own count must be its 7 words, not the 5 of
    // the removed page, and the book's figures must stay as they are.
    await goToPage(page, firstPage.id);
    await expect(
        wordsThisPage,
        "On the remaining page of text, the Leveled Reader should count its 7 words.",
    ).toHaveText("7", { timeout: 15000 });
    await expect(
        wordsInBook,
        "On the remaining page of text, the book should still have 7 words.",
    ).toHaveText("7");
    await expect(
        averagePerSentence,
        "On the remaining page of text, the book should still average 7.0 words per sentence.",
    ).toHaveText("7.0");
});
