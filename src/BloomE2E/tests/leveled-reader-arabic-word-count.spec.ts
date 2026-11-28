// The Leveled Reader counts the words and sentences of Arabic text the way an Arabic reader would:
// Arabic sentence marks end sentences, and vowel marks (harakat) and the Arabic comma do not make
// extra words. Automates the manual test "Arabic Word Count Stats" (Test Case ID 461): paste the
// Arabic sample, and the Leveled Reader should report 7 words, an average of 2.3 per sentence.
//
// The manual case's sample text was in a spreadsheet note that did not survive the move to Notion,
// so this test uses its own: seven Arabic words in three sentences, the same counts the case
// expects. One sentence ends with the Arabic full stop (U+06D4), one with the Arabic question mark
// (U+061F) and one with a plain full stop; the words carry harakat, and one is followed by an
// Arabic comma (U+060C). Were either Arabic sentence mark not taken as the end of a sentence, the
// page would have fewer sentences and a higher average; were the comma taken as one, there would
// be more; were a vowel mark taken as a break between words, there would be more than seven words.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    pasteTextInGroup,
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
        name: "leveled-reader-arabic-word-count",
        languages: ["ar"],
        rightToLeftLanguages: ["ar"],
    },
});

const GROUP = ".bloom-translationGroup";
const LANG = "ar";

/** Seven words, three sentences: 2 + 2 + 3 words. */
const ARABIC_SAMPLE =
    "ذَهَبَ الوَلَدُ۔ " + // "The boy went." ending with the Arabic full stop
    "أَيْنَ الكِتَابُ؟ " + // "Where is the book?" ending with the Arabic question mark
    "القِطَّةُ، الكَلْبُ والبَيْتُ."; // "The cat, the dog and the house."

test("the Leveled Reader counts 7 words, 2.3 per sentence, in the Arabic sample (Test Case ID 461)", async ({
    page,
}) => {
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text");
    const [textPage] = await getContentPages(page);
    // Before enableToolForBook, which goes back into the book, so the page comes up with the book
    // already leveled (see makeBookLeveled).
    await makeBookLeveled(page);
    await enableToolForBook(page, bookFolder, "leveledReader");
    await goToPage(page, textPage.id);
    await expandReaderTool(page, "leveledReader");
    await waitForReaderToolToRestore(page, "leveledReader", bookFolder);
    // Pasting while Bloom reloads the page for the level would be lost.
    await waitForLevelToBeRecordedInTheBook(page, bookFolder);

    await pasteTextInGroup(page, GROUP, LANG, ARABIC_SAMPLE);

    const toolbox = toolboxFrame(page);
    // The page's count is updated after the paste by the tool's markup pass, so wait for it.
    await expect(
        toolbox.getByTestId("leveled-reader-words-this-page"),
        "The Leveled Reader should count 7 words on the page of Arabic text.",
    ).toHaveText("7", { timeout: 15000 });
    await expect(
        toolbox.getByTestId("leveled-reader-sentences-this-page"),
        "The Leveled Reader should count 3 sentences on the page: one ending in the Arabic full " +
            "stop, one in the Arabic question mark, and one in a full stop.",
    ).toHaveText("3");
    // The only page with text, so the book's figures are the page's.
    await expect(
        toolbox.getByTestId("leveled-reader-words-in-book"),
        "The Leveled Reader should count 7 words in the book.",
    ).toHaveText("7");
    await expect(
        toolbox.getByTestId("leveled-reader-average-words-per-sentence"),
        "The Leveled Reader should report an average of 2.3 words per sentence (7 in 3).",
    ).toHaveText("2.3");
});
