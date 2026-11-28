// The Leveled Reader takes a zero-width space (U+200B) as a break between words, the way Thai and
// other scripts written without visible spaces mark word boundaries. Automates the manual test
// "Thai Zero-Width Word Count" (Test Case ID 462): paste "Thisisfourwords", with Thai-style
// zero-width spaces between its words, and the Leveled Reader should count 4 words.
//
// U+200B is Unicode category Cf (Format), not a space separator, so it is not matched by \p{Z} or
// \s; were the reader to split words only on those, the sample would count as one word.

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
        name: "leveled-reader-thai-zero-width-word-count",
        languages: ["th"],
    },
});

const GROUP = ".bloom-translationGroup";
const LANG = "th";

const ZERO_WIDTH_SPACE = String.fromCharCode(0x200b);
/** "Thisisfourwords", with a zero-width space between each of its four words. */
const SAMPLE = ["This", "is", "four", "words"].join(ZERO_WIDTH_SPACE);

test("the Leveled Reader counts 4 words in text whose words are separated by zero-width spaces (Test Case ID 462)", async ({
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

    // pasteTextInGroup checks that the box holds exactly SAMPLE, zero-width spaces included.
    await pasteTextInGroup(page, GROUP, LANG, SAMPLE);

    const toolbox = toolboxFrame(page);
    // The page's count is updated after the paste by the tool's markup pass, so wait for it.
    await expect(
        toolbox.getByTestId("leveled-reader-words-this-page"),
        "The Leveled Reader should count 4 words on the page: the zero-width spaces separate them.",
    ).toHaveText("4", { timeout: 15000 });
    // The only page with text, so the book's count is the page's.
    await expect(
        toolbox.getByTestId("leveled-reader-words-in-book"),
        "The Leveled Reader should count 4 words in the book.",
    ).toHaveText("4");
});
